namespace GameTimeGuard.Core.Clock;

/// <summary>
/// 可信时钟锚点。TrustedUtc 只随真实流逝前进，不跟随被拨动的墙钟。
/// </summary>
public sealed record ClockSample
{
    public bool HasSample { get; init; }
    public DateTimeOffset TrustedUtc { get; init; }
    public long MonotonicMilliseconds { get; init; }
    public DateTimeOffset WallUtc { get; init; }
    public TimeSpan StoredOffset { get; init; }
    public DateOnly LogicalDate { get; init; }
    public bool TamperLatched { get; init; }
    public TamperKind TamperKind { get; init; }
}
