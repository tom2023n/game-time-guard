using GameTimeGuard.Core.Clock;

namespace GameTimeGuard.Core.Quota;

public static class BonusPolicy
{
    public static readonly int[] AllowedMinutes = [15, 30, 60];

    public static (bool Ok, string? Error, UsageSnapshot Usage) TryGrant(UsageSnapshot usage, int minutes)
    {
        if (Array.IndexOf(AllowedMinutes, minutes) < 0)
            return (false, "只能临时加时 15、30 或 60 分钟", usage);
        if (usage.TamperLatched)
            return (false, "检测到时间或记录异常，请先解除锁定再加时", usage);

        return (true, null, usage with
        {
            BonusSeconds = usage.BonusSeconds + minutes * 60,
            TimeUpNotified = false,
            GraceActive = false,
            GraceAnnounced = false,
            GraceStartedUtc = null,
            GraceReason = null
        });
    }

    /// <summary>
    /// 家长确认异常后解除锁定。当天已用时间至少记满额度，剩余时间不恢复。
    /// </summary>
    public static UsageSnapshot Acknowledge(UsageSnapshot usage, int quotaSeconds)
    {
        var floor = Math.Max(0, quotaSeconds) * 1000L;
        var used = Math.Max(usage.UsedMilliseconds, floor);
        if (used > int.MaxValue)
            used = int.MaxValue;
        return usage with
        {
            TamperLatched = false,
            TamperReason = null,
            UsedMilliseconds = (int)used,
            GraceActive = false,
            GraceAnnounced = false,
            GraceStartedUtc = null,
            GraceReason = null
        };
    }

    public static UsageSnapshot LatchRecordTamper(UsageSnapshot usage)
    {
        if (usage.TamperLatched)
            return usage;
        return usage with
        {
            TamperLatched = true,
            TamperReason = TamperKind.RecordTampered.ToString()
        };
    }
}
