namespace GameTimeGuard.Core.Quota;

public static class ReasonCodes
{
    public const string Setup = "setup";
    public const string Ok = "ok";
    public const string Warn = "warn";
    public const string Quota = "quota";
    public const string Window = "window";
    public const string Tamper = "tamper";
}

public static class Phases
{
    public const string Setup = "setup";
    public const string Allowed = "allowed";
    public const string Grace = "grace";
    public const string Terminate = "terminate";
    public const string Exhausted = "exhausted";
}

public enum DecisionKind
{
    SetupRequired,
    Allowed,
    Warn,
    Grace,
    Terminate
}

public sealed record Evaluation
{
    public DecisionKind Kind { get; init; }
    public UsageSnapshot Usage { get; init; } = UsageSnapshot.Fresh(default);
    public string Phase { get; init; } = Phases.Setup;
    public string ReasonCode { get; init; } = ReasonCodes.Setup;
    public int RemainingSeconds { get; init; }
    public int QuotaSeconds { get; init; }
    public int UsedSeconds { get; init; }
    public int BonusSeconds { get; init; }
    public int? WarnMinutes { get; init; }
    public bool ShouldNotifyTimeUp { get; init; }
    public bool GraceNewlyStarted { get; init; }
    public bool TamperNewlyLatched { get; init; }
    public bool WindowOpen { get; init; }
    public int GraceSecondsRemaining { get; init; }
    public string Message { get; init; } = "";
}
