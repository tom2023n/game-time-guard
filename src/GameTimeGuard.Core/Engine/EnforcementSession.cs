using GameTimeGuard.Core;
using GameTimeGuard.Core.Clock;
using GameTimeGuard.Core.Ipc;
using GameTimeGuard.Core.Quota;
using GameTimeGuard.Core.Rules;
using GameTimeGuard.Core.Schedule;
using GameTimeGuard.Core.Security;

namespace GameTimeGuard.Core.Engine;

public sealed record EnforcementResult(
    Evaluation Evaluation,
    ClockSample Clock,
    IReadOnlyList<ProcessMatch> Matched,
    IReadOnlyList<int> TerminatedPids,
    bool StateChanged);

public sealed class EnforcementSession
{
    private readonly IProcessQuery _processes;
    private readonly IProcessTerminator _terminator;
    private readonly IClockSource _clock;
    private readonly TimeSpan? _maxCredit;
    private long? _lastMono;
    private Evaluation? _lastEvaluation;
    private IReadOnlyList<ProcessMatch> _matched = [];

    public EnforcementSession(
        GuardConfig config,
        UsageSnapshot usage,
        ClockSample? clock,
        PlatformCatalog catalog,
        IProcessQuery processes,
        IProcessTerminator terminator,
        IClockSource clockSource,
        TimeSpan? maxCredit = null)
    {
        Config = config;
        Usage = usage;
        Clock = clock;
        Catalog = catalog;
        _processes = processes;
        _terminator = terminator;
        _clock = clockSource;
        _maxCredit = maxCredit;
    }

    public GuardConfig Config { get; private set; }
    public UsageSnapshot Usage { get; private set; }
    public ClockSample? Clock { get; private set; }
    public PlatformCatalog Catalog { get; }

    /// <summary>测试可以调低迭代次数。正式环境保持 PBKDF2 默认值。</summary>
    public int PasswordIterations { get; set; } = ParentPassword.DefaultIterations;

    public EnforcementResult Poll(DateTimeOffset? ntpUtc = null)
    {
        var wall = _clock.GetWallUtc();
        var mono = _clock.GetMonotonicMilliseconds();
        var offset = _clock.GetUtcOffset();
        var elapsed = TimeSpan.Zero;
        if (_lastMono is long previous && mono >= previous)
            elapsed = TimeSpan.FromMilliseconds(mono - previous);
        _lastMono = mono;

        var beforeUsage = Usage;
        var beforeClock = Clock;
        var beforeConfig = Config.ProtectionState + Config.PauseUntilUtc;
        var sample = TrustedClock.Observe(Clock, wall, mono, offset, ntpUtc);
        Config = ProtectionControl.Normalize(Config, sample.TrustedUtc);
        var matched = RuleMatcher.MatchAll(_processes.Query(), Catalog.Enabled(Config.EnabledRuleIds), Config.CustomGames);
        var seen = string.Join(";", matched.Select(match => match.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase));
        Evaluation evaluation;
        if (!Config.SetupComplete || ProtectionControl.IsIdle(Config, sample.TrustedUtc))
        {
            evaluation = Config.SetupComplete
                ? IdleEvaluation(sample, seen)
                : QuotaEngine.Evaluate(Usage, Config, sample, false, TimeSpan.Zero, _maxCredit);
        }
        else
        {
            if (seen != Usage.LastSeenGames)
                Usage = Usage with { LastSeenGames = seen };
            evaluation = QuotaEngine.Evaluate(Usage, Config, sample, matched.Count > 0, elapsed, _maxCredit);
        }

        var terminated = new List<int>();
        if (evaluation.Kind == DecisionKind.Terminate)
        {
            foreach (var match in matched)
            {
                if (WindowsPath.IsUnsafe(match.Process.ExecutablePath))
                    continue;
                _terminator.Terminate(match.Process.ProcessId);
                terminated.Add(match.Process.ProcessId);
            }
        }

        Usage = evaluation.Usage;
        Clock = sample;
        _lastEvaluation = evaluation;
        _matched = matched;
        var changed = beforeUsage != Usage || beforeClock != sample || beforeConfig != Config.ProtectionState + Config.PauseUntilUtc;
        return new EnforcementResult(evaluation, sample, matched, terminated, changed);
    }

    public StatusReport BuildStatus()
    {
        var evaluation = _lastEvaluation;
        var quota = evaluation?.QuotaSeconds ?? Math.Max(0, Config.MinutesFor(Usage.LogicalDate.DayOfWeek)) * 60;
        var used = evaluation?.UsedSeconds ?? Usage.UsedMilliseconds / 1000;
        var remaining = evaluation?.RemainingSeconds ?? (Usage.TamperLatched ? 0 : Math.Max(0, quota + Usage.BonusSeconds - used));
        return new StatusReport
        {
            SetupComplete = Config.SetupComplete,
            ServiceOnline = true,
            LogicalDate = Usage.LogicalDate.ToString("yyyy-MM-dd"),
            UsedSeconds = used,
            QuotaSeconds = quota,
            BonusSeconds = evaluation?.BonusSeconds ?? (Usage.TamperLatched ? 0 : Usage.BonusSeconds),
            RemainingSeconds = remaining,
            GameRunning = _matched.Count > 0,
            RunningGames = _matched.Select(match => match.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray(),
            WithinWindow = evaluation?.WindowOpen ?? true,
            TamperLatched = Usage.TamperLatched,
            TamperReason = Usage.TamperReason,
            Phase = evaluation?.Phase ?? (Config.SetupComplete ? (Usage.TamperLatched ? Phases.Exhausted : Phases.Allowed) : Phases.Setup),
            GraceSecondsRemaining = evaluation?.GraceSecondsRemaining ?? 0,
            ReasonCode = evaluation?.ReasonCode ?? ReasonCodes.Ok,
            Message = evaluation?.Message ?? "",
            Version = AppVersion.Current,
            ProtectionState = Config.ProtectionState,
            PauseUntilUtc = Config.PauseUntilUtc ?? "",
            TraySuppressed = Config.TraySuppressed
        };
    }

    public SettingsSnapshot BuildSettings()
    {
        var settings = DefaultPolicy.FromConfig(Config);
        return new SettingsSnapshot
        {
            DailyMinutes = settings.DailyMinutes,
            PlayWindowsUnrestricted = settings.PlayWindowsUnrestricted,
            PlayWindows = settings.PlayWindows,
            EnabledRuleIds = settings.EnabledRuleIds,
            CustomGames = settings.CustomGames,
            GraceSeconds = settings.GraceSeconds,
            GapPolicy = settings.GapPolicy,
            GapPenaltyMinutes = settings.GapPenaltyMinutes,
            EmailEnabled = settings.EmailEnabled,
            SmtpHost = settings.SmtpHost,
            SmtpPort = settings.SmtpPort,
            SmtpUseSsl = settings.SmtpUseSsl,
            SmtpUser = settings.SmtpUser,
            NotifyAddress = settings.NotifyAddress,
            NotifyTamper = settings.NotifyTamper,
            NotifyTimeUp = settings.NotifyTimeUp,
            NotifyDailySummary = settings.NotifyDailySummary,
            HasSmtpPassword = !string.IsNullOrEmpty(Config.SmtpPasswordProtected),
            Catalog = Catalog.Rules.Select(rule => new RuleInfo
            {
                Id = rule.Id,
                DisplayName = rule.DisplayName,
                Description = rule.Description,
                Enabled = Config.EnabledRuleIds.Contains(rule.Id, StringComparer.OrdinalIgnoreCase)
            }).ToList()
        };
    }

    public string? CompleteSetup(string password, SettingsUpdate settings)
    {
        if (Config.SetupComplete)
            return "已经完成初始化";
        var choice = ValidatePasswordChoice(password);
        if (choice is not null)
            return choice;
        var errors = ConfigValidator.Validate(settings);
        if (errors.Count > 0)
            return string.Join("\n", errors);

        var next = Config.Clone();
        DefaultPolicy.Apply(next, settings);
        next.ParentPassword = ParentPassword.Hash(password, PasswordIterations);
        next.SetupComplete = true;
        next.Sequence++;
        Config = next;
        return null;
    }

    public string? UpdateSettings(string password, SettingsUpdate settings, ISecretProtector? secrets = null)
    {
        var denied = RequirePassword(password);
        if (denied is not null)
            return denied;
        var errors = ConfigValidator.Validate(settings);
        if (errors.Count > 0)
            return string.Join("\n", errors);

        var next = Config.Clone();
        var hash = next.ParentPassword;
        var sequence = next.Sequence;
        var lastUsage = next.LastUsageSequence;
        var setup = next.SetupComplete;
        var protectedPassword = next.SmtpPasswordProtected;
        var state = next.ProtectionState;
        var pauseUntil = next.PauseUntilUtc;
        var tray = next.TraySuppressed;
        var armed = next.UninstallArmedUntilUtc;
        var summary = next.LastSummaryDate;
        DefaultPolicy.Apply(next, settings);
        if (!string.IsNullOrEmpty(settings.SmtpPassword))
        {
            if (secrets is null)
                return "无法加密邮箱密码";
            protectedPassword = secrets.Protect(settings.SmtpPassword);
        }

        next.ParentPassword = hash;
        next.SetupComplete = setup;
        next.LastUsageSequence = lastUsage;
        next.SmtpPasswordProtected = protectedPassword;
        next.ProtectionState = state;
        next.PauseUntilUtc = pauseUntil;
        next.TraySuppressed = tray;
        next.UninstallArmedUntilUtc = armed;
        next.LastSummaryDate = summary;
        next.Sequence = sequence + 1;
        var mailError = MailNotices.Validate(next);
        if (mailError is not null)
            return mailError;
        Config = next;
        return null;
    }

    public string? Pause(string password, int minutes)
    {
        var denied = RequirePassword(password);
        if (denied is not null)
            return denied;
        if (Clock is not { HasSample: true } clock)
            return "还没有可信时间，请稍后再试";
        var error = ProtectionControl.Pause(Config, clock.TrustedUtc, minutes);
        return error;
    }

    public string? StopProtection(string password, bool suppressTray)
    {
        var denied = RequirePassword(password);
        if (denied is not null)
            return denied;
        ProtectionControl.Stop(Config, suppressTray);
        return null;
    }

    public string? ResumeProtection(string password)
    {
        var denied = RequirePassword(password);
        if (denied is not null)
            return denied;
        ProtectionControl.Resume(Config);
        return null;
    }

    public string? ArmUninstall(string password, DateTimeOffset trustedUtc)
    {
        var denied = RequirePassword(password);
        if (denied is not null)
            return denied;
        ProtectionControl.ArmUninstall(Config, trustedUtc, TimeSpan.FromMinutes(10));
        return null;
    }

    public string? ChangePassword(string currentPassword, string newPassword)
    {
        var denied = RequirePassword(currentPassword);
        if (denied is not null)
            return denied;
        var choice = ValidatePasswordChoice(newPassword);
        if (choice is not null)
            return choice;
        Config.ParentPassword = ParentPassword.Hash(newPassword, PasswordIterations);
        Config.Sequence++;
        return null;
    }

    public string? GrantBonus(string password, int minutes)
    {
        var denied = RequirePassword(password);
        if (denied is not null)
            return denied;
        var (ok, error, updated) = BonusPolicy.TryGrant(Usage, minutes);
        if (!ok)
            return error;
        Usage = updated;
        return null;
    }

    public string? AcknowledgeTamper(string password)
    {
        var denied = RequirePassword(password);
        if (denied is not null)
            return denied;
        if (!Usage.TamperLatched && Clock is not { TamperLatched: true })
            return "当前没有需要解除的锁定";

        var quota = Math.Max(0, Config.MinutesFor(Usage.LogicalDate.DayOfWeek)) * 60;
        Usage = BonusPolicy.Acknowledge(Usage, quota);
        if (Clock is { HasSample: true } clock)
            Clock = TrustedClock.Acknowledge(clock, _clock.GetUtcOffset());
        return null;
    }

    public string? Authorize(string password) => RequirePassword(password);

    public void NoteUsagePersisted(long sequence)
    {
        Usage = Usage with { Sequence = sequence };
        if (sequence > Config.LastUsageSequence)
            Config.LastUsageSequence = sequence;
    }

    private string? RequirePassword(string? password)
    {
        if (!Config.SetupComplete || Config.ParentPassword is null)
            return "还没有设置家长密码";
        if (!ParentPassword.Verify(password, Config.ParentPassword))
            return "密码不正确";
        return null;
    }

    private Evaluation IdleEvaluation(ClockSample sample, string seen)
    {
        Usage = Usage with { LastSeenGames = seen, GraceActive = false, GraceAnnounced = false, GraceStartedUtc = null, GraceReason = null };
        var stopped = Config.ProtectionState.Equals(ProtectionControl.Stopped, StringComparison.OrdinalIgnoreCase);
        return new Evaluation
        {
            Kind = DecisionKind.Allowed,
            Usage = Usage,
            Phase = stopped ? "stopped" : "paused",
            ReasonCode = stopped ? "stopped" : "paused",
            Message = stopped ? "家长已停止保护。要恢复请输入家长密码。" : "家长已暂停保护，到点后会自动恢复。",
            WindowOpen = true
        };
    }

    private static string? ValidatePasswordChoice(string password)
    {
        try
        {
            ParentPassword.ValidateChoice(password);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }
}
