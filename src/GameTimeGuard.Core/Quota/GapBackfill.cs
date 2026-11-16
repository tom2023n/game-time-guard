namespace GameTimeGuard.Core.Quota;

public enum GapChargeKind
{
    None,
    Observed,
    Penalty,
    FullGap
}

public sealed record ProcessSighting(string Name, DateTimeOffset? StartedUtc, bool StillRunning);

public sealed record GapRequest(
    DateTimeOffset GapStart,
    DateTimeOffset GapEnd,
    bool UserLoggedOn,
    DateTimeOffset? ParentIdleUntil,
    IReadOnlyList<ProcessSighting> Sightings,
    string Policy,
    int PenaltyMinutes);

public sealed record GapDecision(int ChargeSeconds, GapChargeKind Kind, string Message);

/// <summary>
/// 服务停着的时候，按还能看到的进程启动时间估算玩了多久。看不准就按家长设置的罚分，或把整段缺口都算上。
/// 家长自己暂停或停止的时段不记。
/// </summary>
public static class GapBackfill
{
    public static readonly TimeSpan IgnoreShorterThan = TimeSpan.FromMinutes(2);

    public static GapDecision Estimate(GapRequest request)
    {
        if (request.GapEnd <= request.GapStart)
            return None("没有中断");

        var gap = request.GapEnd - request.GapStart;
        if (gap < IgnoreShorterThan)
            return None("中断太短，不另记账");

        var start = request.GapStart;
        if (request.ParentIdleUntil is DateTimeOffset idleUntil)
        {
            if (idleUntil >= request.GapEnd)
                return None("这段时间是家长关掉的保护，不记账");
            if (idleUntil > start)
                start = idleUntil;
        }

        var remaining = request.GapEnd - start;
        if (remaining < IgnoreShorterThan)
            return None("家长暂停结束后的中断太短，不另记账");

        if (!request.UserLoggedOn)
            return None("中断时没有人登录，不记账");

        var observed = TimeSpan.Zero;
        foreach (var sighting in request.Sightings)
        {
            if (!sighting.StillRunning || sighting.StartedUtc is null)
                continue;
            var from = sighting.StartedUtc.Value < start ? start : sighting.StartedUtc.Value;
            if (from >= request.GapEnd)
                continue;
            var overlap = request.GapEnd - from;
            if (overlap > observed)
                observed = overlap;
        }

        if (observed > TimeSpan.Zero)
        {
            return Charge(observed, remaining, GapChargeKind.Observed, "按仍在运行的游戏进程，补记中断期间的时间");
        }

        if (request.Policy.Equals("full", StringComparison.OrdinalIgnoreCase))
            return Charge(remaining, remaining, GapChargeKind.FullGap, "无法确定中断时有没有玩游戏，已按整段中断记账");

        var penalty = TimeSpan.FromMinutes(Math.Max(0, request.PenaltyMinutes));
        return Charge(penalty, remaining, GapChargeKind.Penalty, "无法确定中断时有没有玩游戏，已记入一笔罚时");
    }

    private static GapDecision None(string message) => new(0, GapChargeKind.None, message);

    private static GapDecision Charge(TimeSpan wanted, TimeSpan cap, GapChargeKind kind, string message)
    {
        if (wanted > cap)
            wanted = cap;
        if (wanted < TimeSpan.Zero)
            wanted = TimeSpan.Zero;
        return new((int)Math.Ceiling(wanted.TotalSeconds), kind, message);
    }
}
