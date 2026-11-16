using GameTimeGuard.Core.Clock;
using GameTimeGuard.Core.Schedule;

namespace GameTimeGuard.Core.Quota;

public static class QuotaEngine
{
    public const int Warn10Seconds = 10 * 60;
    public const int Warn5Seconds = 5 * 60;
    public static readonly TimeSpan DefaultMaxCredit = TimeSpan.FromMinutes(2);

    public static Evaluation Evaluate(
        UsageSnapshot usage,
        GuardConfig config,
        ClockSample clock,
        bool gameRunning,
        TimeSpan elapsedAwake,
        TimeSpan? maxCredit = null)
    {
        if (!clock.HasSample)
            throw new ArgumentException("需要有效的时钟采样", nameof(clock));

        var creditCap = maxCredit ?? DefaultMaxCredit;
        if (elapsedAwake < TimeSpan.Zero)
            elapsedAwake = TimeSpan.Zero;
        if (elapsedAwake > creditCap)
            elapsedAwake = creditCap;

        if (!config.SetupComplete)
        {
            return new Evaluation
            {
                Kind = DecisionKind.SetupRequired,
                Usage = usage,
                Phase = Phases.Setup,
                ReasonCode = ReasonCodes.Setup,
                Message = "还没有完成家长初始化，暂时不会限制游戏。"
            };
        }

        var next = usage;
        if (clock.LogicalDate > next.LogicalDate)
            next = UsageSnapshot.FreshDay(clock.LogicalDate, next.TamperLatched, next.TamperReason);

        var tamperWas = next.TamperLatched;
        if (clock.TamperLatched && !next.TamperLatched)
        {
            next = next with
            {
                TamperLatched = true,
                TamperReason = clock.TamperKind == TamperKind.None ? TamperKind.RecordTampered.ToString() : clock.TamperKind.ToString()
            };
        }

        var scheduleMinutes = Math.Max(0, config.MinutesFor(next.LogicalDate.DayOfWeek));
        var scheduleSeconds = scheduleMinutes * 60;
        var quotaSeconds = scheduleSeconds;
        if (!next.TamperLatched)
            quotaSeconds += Math.Max(0, next.BonusSeconds);

        var local = clock.TrustedUtc.ToOffset(clock.StoredOffset);
        var localTime = TimeOnly.FromTimeSpan(local.TimeOfDay);
        var windowOpen = WindowEvaluator.IsOpen(config, next.LogicalDate.DayOfWeek, localTime);

        if (gameRunning && windowOpen && !next.TamperLatched)
        {
            var add = (int)Math.Min(int.MaxValue, Math.Floor(elapsedAwake.TotalMilliseconds));
            if (add > 0)
            {
                var used = (long)next.UsedMilliseconds + add;
                if (used > int.MaxValue)
                    used = int.MaxValue;
                next = next with { UsedMilliseconds = (int)used };
            }
        }

        var usedSeconds = next.UsedMilliseconds / 1000;
        var overQuota = next.TamperLatched || usedSeconds >= quotaSeconds;
        var remaining = next.TamperLatched ? 0 : Math.Max(0, quotaSeconds - usedSeconds);

        string? violation = null;
        if (gameRunning)
        {
            if (next.TamperLatched)
                violation = ReasonCodes.Tamper;
            else if (overQuota)
                violation = ReasonCodes.Quota;
            else if (!windowOpen)
                violation = ReasonCodes.Window;
        }

        int? warn = null;
        if (violation is null && gameRunning && !next.TamperLatched)
        {
            if (remaining <= Warn5Seconds && !next.Warned5)
            {
                warn = 5;
                next = next with { Warned5 = true, Warned10 = true };
            }
            else if (remaining <= Warn10Seconds && !next.Warned10)
            {
                warn = 10;
                next = next with { Warned10 = true };
            }
        }

        var graceSeconds = Math.Clamp(config.GraceSeconds, 0, 300);
        var timeUp = false;
        var graceNew = false;
        DecisionKind kind;

        if (violation is null)
        {
            next = ClearGrace(next);
            kind = warn is null ? DecisionKind.Allowed : DecisionKind.Warn;
        }
        else if (!next.GraceActive)
        {
            next = next with
            {
                GraceActive = true,
                GraceAnnounced = true,
                GraceStartedUtc = clock.TrustedUtc,
                GraceReason = violation
            };
            graceNew = true;
            if (graceSeconds == 0 || clock.TrustedUtc >= clock.TrustedUtc.AddSeconds(graceSeconds) && graceSeconds == 0)
            {
                kind = DecisionKind.Terminate;
                timeUp = !usage.TimeUpNotified && !next.TimeUpNotified;
                next = next with { TimeUpNotified = true };
            }
            else
            {
                kind = DecisionKind.Grace;
            }
        }
        else
        {
            var started = next.GraceStartedUtc ?? clock.TrustedUtc;
            if (clock.TrustedUtc >= started.AddSeconds(graceSeconds))
            {
                kind = DecisionKind.Terminate;
                timeUp = !next.TimeUpNotified;
                next = next with { TimeUpNotified = true };
            }
            else
            {
                kind = DecisionKind.Grace;
            }
        }

        if (graceSeconds == 0 && violation is not null && kind != DecisionKind.Terminate)
        {
            kind = DecisionKind.Terminate;
            timeUp = !next.TimeUpNotified;
            next = next with { TimeUpNotified = true, GraceActive = true, GraceAnnounced = true, GraceReason = violation };
        }

        var displayedQuota = next.TamperLatched ? scheduleSeconds : quotaSeconds;
        return new Evaluation
        {
            Kind = kind,
            Usage = next,
            Phase = PhaseFor(kind, remaining, next.TamperLatched),
            ReasonCode = violation ?? (warn is null ? ReasonCodes.Ok : ReasonCodes.Warn),
            RemainingSeconds = remaining,
            QuotaSeconds = displayedQuota,
            UsedSeconds = usedSeconds,
            BonusSeconds = next.TamperLatched ? 0 : next.BonusSeconds,
            WarnMinutes = warn,
            ShouldNotifyTimeUp = timeUp,
            GraceNewlyStarted = graceNew,
            TamperNewlyLatched = next.TamperLatched && !tamperWas,
            WindowOpen = windowOpen,
            GraceSecondsRemaining = GraceLeft(next, clock, graceSeconds),
            Message = MessageFor(kind, violation, warn, graceSeconds, remaining)
        };
    }

    private static UsageSnapshot ClearGrace(UsageSnapshot usage) => usage with
    {
        GraceActive = false,
        GraceAnnounced = false,
        GraceStartedUtc = null,
        GraceReason = null
    };

    private static string PhaseFor(DecisionKind kind, int remaining, bool tamper) => kind switch
    {
        DecisionKind.SetupRequired => Phases.Setup,
        DecisionKind.Grace => Phases.Grace,
        DecisionKind.Terminate => Phases.Terminate,
        _ when tamper || remaining == 0 => Phases.Exhausted,
        _ => Phases.Allowed
    };

    private static int GraceLeft(UsageSnapshot usage, ClockSample clock, int graceSeconds)
    {
        if (!usage.GraceActive || usage.GraceStartedUtc is null)
            return 0;
        var left = usage.GraceStartedUtc.Value.AddSeconds(graceSeconds) - clock.TrustedUtc;
        var seconds = (int)Math.Ceiling(left.TotalSeconds);
        return Math.Max(0, seconds);
    }

    private static string MessageFor(DecisionKind kind, string? violation, int? warn, int graceSeconds, int remaining)
    {
        if (warn == 10)
            return "今天还剩 10 分钟游戏时间。";
        if (warn == 5)
            return "今天还剩 5 分钟游戏时间。";
        if (kind is DecisionKind.Grace or DecisionKind.Terminate)
        {
            var why = violation switch
            {
                ReasonCodes.Window => "现在不在允许的游戏时段",
                ReasonCodes.Tamper => "检测到系统时间或游戏记录被改动",
                _ => "今天的游戏时间已经用完"
            };
            return kind == DecisionKind.Grace
                ? $"{why}，{graceSeconds} 秒后关闭游戏。"
                : $"{why}，正在关闭游戏。";
        }

        if (remaining == 0)
            return "今天的游戏时间已经用完。";
        return "今天还可以继续玩。";
    }
}
