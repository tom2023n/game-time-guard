using GameTimeGuard.Core.Clock;
using GameTimeGuard.Core.Quota;
using GameTimeGuard.Core.Schedule;

namespace GameTimeGuard.Core.Tests;

public class QuotaEngineTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);

    [Fact]
    public void Setup_is_not_enforced()
    {
        var config = DefaultPolicy.Uninitialized();
        var result = QuotaEngine.Evaluate(UsageSnapshot.Fresh(Monday), config, ClockAt(12, 0), true, TimeSpan.FromMinutes(1));
        Assert.Equal(DecisionKind.SetupRequired, result.Kind);
        Assert.Equal(0, result.Usage.UsedMilliseconds);
    }

    [Fact]
    public void Counts_only_while_a_game_is_running_inside_the_window()
    {
        var config = Ready(minutes: 120);
        var usage = UsageSnapshot.Fresh(Monday);
        var clock = ClockAt(12, 0);

        var idle = QuotaEngine.Evaluate(usage, config, clock, false, TimeSpan.FromMinutes(1));
        Assert.Equal(0, idle.Usage.UsedMilliseconds);

        var playing = QuotaEngine.Evaluate(usage, config, clock, true, TimeSpan.FromSeconds(75));
        Assert.Equal(75_000, playing.Usage.UsedMilliseconds);
        Assert.Equal(120 * 60 - 75, playing.RemainingSeconds);
        Assert.Equal(DecisionKind.Allowed, playing.Kind);
    }

    [Fact]
    public void Outside_window_does_not_consume_quota_and_starts_grace()
    {
        var config = Ready(minutes: 120, window: "18:00-21:00");
        var result = QuotaEngine.Evaluate(UsageSnapshot.Fresh(Monday), config, ClockAt(9, 0), true, TimeSpan.FromMinutes(1));

        Assert.Equal(0, result.Usage.UsedMilliseconds);
        Assert.Equal(DecisionKind.Grace, result.Kind);
        Assert.Equal(ReasonCodes.Window, result.ReasonCode);
        Assert.True(result.GraceNewlyStarted);
        Assert.Equal(30, result.GraceSecondsRemaining);
    }

    [Fact]
    public void Warns_at_10_and_5_minutes_once()
    {
        var config = Ready(minutes: 120);
        var usage = UsageSnapshot.Fresh(Monday) with { UsedMilliseconds = (110 * 60) * 1000 };
        var first = QuotaEngine.Evaluate(usage, config, ClockAt(12, 0), true, TimeSpan.Zero);
        Assert.Equal(10, first.WarnMinutes);
        Assert.True(first.Usage.Warned10);

        var again = QuotaEngine.Evaluate(first.Usage, config, ClockAt(12, 0), true, TimeSpan.Zero);
        Assert.Null(again.WarnMinutes);

        var nearEnd = again.Usage with { UsedMilliseconds = (115 * 60) * 1000 };
        var second = QuotaEngine.Evaluate(nearEnd, config, ClockAt(12, 1), true, TimeSpan.Zero);
        Assert.Equal(5, second.WarnMinutes);
        Assert.True(second.Usage.Warned5);

        var repeat = QuotaEngine.Evaluate(second.Usage, config, ClockAt(12, 1), true, TimeSpan.Zero);
        Assert.Null(repeat.WarnMinutes);
    }

    [Fact]
    public void A_large_drop_announces_the_more_urgent_warning_only()
    {
        var config = Ready(minutes: 120);
        var usage = UsageSnapshot.Fresh(Monday) with { UsedMilliseconds = (100 * 60) * 1000 };
        var result = QuotaEngine.Evaluate(usage, config, ClockAt(12, 0), true, TimeSpan.FromMinutes(18), TimeSpan.FromMinutes(20));
        Assert.Equal(5, result.WarnMinutes);
        Assert.True(result.Usage.Warned10);
        Assert.True(result.Usage.Warned5);
    }

    [Fact]
    public void Quota_starts_grace_then_terminates()
    {
        var config = Ready(minutes: 30);
        var usage = UsageSnapshot.Fresh(Monday) with { UsedMilliseconds = 29 * 60 * 1000 };
        var clock = ClockAt(12, 0);
        var grace = QuotaEngine.Evaluate(usage, config, clock, true, TimeSpan.FromMinutes(1));

        Assert.Equal(DecisionKind.Grace, grace.Kind);
        Assert.Equal(ReasonCodes.Quota, grace.ReasonCode);
        Assert.Equal(0, grace.RemainingSeconds);
        Assert.True(grace.Usage.GraceActive);

        var still = QuotaEngine.Evaluate(grace.Usage, config, ClockAt(12, 0, 29), true, TimeSpan.FromSeconds(5));
        Assert.Equal(DecisionKind.Grace, still.Kind);
        Assert.False(still.ShouldNotifyTimeUp);

        var done = QuotaEngine.Evaluate(grace.Usage, config, ClockAt(12, 0, 30), true, TimeSpan.FromSeconds(5));
        Assert.Equal(DecisionKind.Terminate, done.Kind);
        Assert.True(done.ShouldNotifyTimeUp);
        Assert.True(done.Usage.TimeUpNotified);

        var repeat = QuotaEngine.Evaluate(done.Usage, config, ClockAt(12, 0, 40), true, TimeSpan.FromSeconds(5));
        Assert.Equal(DecisionKind.Terminate, repeat.Kind);
        Assert.False(repeat.ShouldNotifyTimeUp);
    }

    [Fact]
    public void Closing_the_game_cancels_grace()
    {
        var config = Ready(minutes: 30);
        var usage = UsageSnapshot.Fresh(Monday) with { UsedMilliseconds = 30 * 60 * 1000 };
        var grace = QuotaEngine.Evaluate(usage, config, ClockAt(12, 0), true, TimeSpan.Zero);
        var stopped = QuotaEngine.Evaluate(grace.Usage, config, ClockAt(12, 0, 10), false, TimeSpan.FromSeconds(10));

        Assert.Equal(DecisionKind.Allowed, stopped.Kind);
        Assert.False(stopped.Usage.GraceActive);
        Assert.Equal(Phases.Exhausted, stopped.Phase);
    }

    [Fact]
    public void New_logical_day_resets_usage_and_a_backward_date_does_not()
    {
        var config = Ready(minutes: 120);
        var used = UsageSnapshot.Fresh(Monday) with
        {
            UsedMilliseconds = 50 * 60 * 1000,
            BonusSeconds = 900,
            Warned10 = true
        };
        var tuesday = QuotaEngine.Evaluate(used, config, ClockOn(new DateOnly(2026, 11, 17), 12, 0), true, TimeSpan.FromSeconds(30));

        Assert.Equal(new DateOnly(2026, 11, 17), tuesday.Usage.LogicalDate);
        Assert.Equal(30_000, tuesday.Usage.UsedMilliseconds);
        Assert.Equal(0, tuesday.Usage.BonusSeconds);
        Assert.False(tuesday.Usage.Warned10);

        var rolledBack = QuotaEngine.Evaluate(tuesday.Usage, config, ClockOn(Monday, 23, 0), false, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 11, 17), rolledBack.Usage.LogicalDate);
        Assert.Equal(30_000, rolledBack.Usage.UsedMilliseconds);
    }

    [Fact]
    public void Tamper_treats_quota_as_used_and_notifies_once()
    {
        var config = Ready(minutes: 120);
        var clock = ClockAt(12, 0) with { TamperLatched = true, TamperKind = TamperKind.ClockRollback };
        var result = QuotaEngine.Evaluate(UsageSnapshot.Fresh(Monday), config, clock, true, TimeSpan.FromMinutes(1));

        Assert.True(result.TamperNewlyLatched);
        Assert.Equal(0, result.RemainingSeconds);
        Assert.Equal(0, result.Usage.UsedMilliseconds);
        Assert.Equal(DecisionKind.Grace, result.Kind);
        Assert.Equal(ReasonCodes.Tamper, result.ReasonCode);

        var nextDay = QuotaEngine.Evaluate(result.Usage, config, clock with { LogicalDate = Monday.AddDays(1), TrustedUtc = clock.TrustedUtc.AddDays(1) }, false, TimeSpan.Zero);
        Assert.True(nextDay.Usage.TamperLatched);
        Assert.False(nextDay.TamperNewlyLatched);
        Assert.Equal(0, nextDay.RemainingSeconds);
    }

    [Fact]
    public void Bonus_extends_the_day_and_is_rejected_while_locked()
    {
        var config = Ready(minutes: 10);
        var usage = UsageSnapshot.Fresh(Monday) with { UsedMilliseconds = 10 * 60 * 1000 };
        var (ok, error, granted) = BonusPolicy.TryGrant(usage, 15);
        Assert.True(ok);
        Assert.Null(error);

        var result = QuotaEngine.Evaluate(granted, config, ClockAt(12, 0), true, TimeSpan.FromSeconds(30));
        Assert.Equal(DecisionKind.Allowed, result.Kind);
        Assert.Equal(15 * 60 - 30, result.RemainingSeconds);

        var locked = usage with { TamperLatched = true };
        var (denied, _, _) = BonusPolicy.TryGrant(locked, 15);
        Assert.False(denied);

        var acked = BonusPolicy.Acknowledge(locked, 10 * 60);
        Assert.False(acked.TamperLatched);
        Assert.Equal(10 * 60 * 1000, acked.UsedMilliseconds);
    }

    [Fact]
    public void Single_tick_credit_is_capped()
    {
        var config = Ready(minutes: 120);
        var result = QuotaEngine.Evaluate(UsageSnapshot.Fresh(Monday), config, ClockAt(12, 0), true, TimeSpan.FromHours(3));
        Assert.Equal((int)QuotaEngine.DefaultMaxCredit.TotalMilliseconds, result.Usage.UsedMilliseconds);
    }

    private static GuardConfig Ready(int minutes, string? window = null)
    {
        var config = DefaultPolicy.Uninitialized();
        config.SetupComplete = true;
        config.DailyMinutes = DayNames.All.Select(day => new DayQuota { Day = day, Minutes = minutes }).ToList();
        if (window is null)
            return config;
        Assert.True(WindowText.TryParse(window, out var ranges, out _));
        config.PlayWindowsUnrestricted = false;
        config.PlayWindows = DayNames.All.Select(day => new DayWindowSet { Day = day, Ranges = ranges }).ToList();
        return config;
    }

    private static ClockSample ClockAt(int hour, int minute, int second = 0) => ClockOn(Monday, hour, minute, second);

    private static ClockSample ClockOn(DateOnly date, int hour, int minute, int second = 0)
    {
        var local = new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, second, TimeSpan.FromHours(8));
        return new ClockSample
        {
            HasSample = true,
            TrustedUtc = local.ToUniversalTime(),
            WallUtc = local.ToUniversalTime(),
            MonotonicMilliseconds = 1,
            StoredOffset = TimeSpan.FromHours(8),
            LogicalDate = date
        };
    }
}
