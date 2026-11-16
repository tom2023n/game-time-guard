using GameTimeGuard.Core.Clock;

namespace GameTimeGuard.Core.Quota;

public sealed record UsageSnapshot
{
    public DateOnly LogicalDate { get; init; }
    public int UsedMilliseconds { get; init; }
    public int BonusSeconds { get; init; }
    public bool Warned10 { get; init; }
    public bool Warned5 { get; init; }
    public bool TimeUpNotified { get; init; }
    public bool TamperLatched { get; init; }
    public string? TamperReason { get; init; }
    public bool GraceActive { get; init; }
    public bool GraceAnnounced { get; init; }
    public DateTimeOffset? GraceStartedUtc { get; init; }
    public string? GraceReason { get; init; }
    public long Sequence { get; init; }
    public string LastSeenGames { get; init; } = "";

    public static UsageSnapshot Fresh(DateOnly date) => new() { LogicalDate = date };

    public static UsageSnapshot FreshDay(DateOnly date, bool tamperLatched, string? tamperReason) => new()
    {
        LogicalDate = date,
        TamperLatched = tamperLatched,
        TamperReason = tamperLatched ? tamperReason : null
    };
}
