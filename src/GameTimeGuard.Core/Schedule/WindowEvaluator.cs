using System.Globalization;

namespace GameTimeGuard.Core.Schedule;

public static class WindowEvaluator
{
    /// <summary>
    /// 结束时刻不包含在内。22:00-02:00 表示跨过午夜。未限制时段时全天可玩。
    /// 限制时段但当天没有任何区间时，整天都不能玩。
    /// </summary>
    public static bool IsOpen(GuardConfig config, DayOfWeek day, TimeOnly localTime)
    {
        if (config.PlayWindowsUnrestricted)
            return true;

        var key = DayNames.From(day);
        DayWindowSet? set = null;
        foreach (var candidate in config.PlayWindows)
        {
            if (candidate.Day.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                set = candidate;
                break;
            }
        }

        if (set is null || set.Ranges.Count == 0)
            return false;

        foreach (var range in set.Ranges)
        {
            if (!TryParse(range.Start, out var start) || !TryParse(range.End, out var end))
                continue;
            if (start == end)
                continue;
            if (start < end)
            {
                if (localTime >= start && localTime < end)
                    return true;
            }
            else if (localTime >= start || localTime < end)
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryParse(string? text, out TimeOnly time)
    {
        return TimeOnly.TryParseExact(
            text?.Trim() ?? "",
            ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);
    }
}
