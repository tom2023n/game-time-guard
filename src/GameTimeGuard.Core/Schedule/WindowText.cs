using System.Globalization;

namespace GameTimeGuard.Core.Schedule;

public static class WindowText
{
    public static bool TryParse(string? text, out List<TimeRange> ranges, out string? error)
    {
        ranges = [];
        error = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var parts = text.Split([',', '，', ';', '；', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 8)
        {
            error = "每天最多 8 个时段";
            return false;
        }

        foreach (var part in parts)
        {
            var pieces = SplitRange(part);
            if (pieces is null)
            {
                error = $"无法识别时段「{part}」。请写成 18:00-21:00";
                return false;
            }

            if (!WindowEvaluator.TryParse(pieces.Value.Start, out var start) ||
                !WindowEvaluator.TryParse(pieces.Value.End, out var end))
            {
                error = $"时间格式不正确：「{part}」。请使用 24 小时制，例如 18:00-21:00";
                return false;
            }

            if (start == end)
            {
                error = "时段的开始和结束不能相同";
                return false;
            }

            ranges.Add(new TimeRange
            {
                Start = start.ToString("HH:mm", CultureInfo.InvariantCulture),
                End = end.ToString("HH:mm", CultureInfo.InvariantCulture)
            });
        }

        return true;
    }

    public static string Format(IReadOnlyList<TimeRange>? ranges)
    {
        if (ranges is null || ranges.Count == 0)
            return "";
        return string.Join(", ", ranges.Select(range => $"{range.Start}-{range.End}"));
    }

    private static (string Start, string End)? SplitRange(string part)
    {
        foreach (var separator in new[] { "—", "–", "~", "～", "-" })
        {
            var index = part.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0 || index >= part.Length - separator.Length)
                continue;
            var start = part[..index].Trim();
            var end = part[(index + separator.Length)..].Trim();
            if (start.Length > 0 && end.Length > 0)
                return (start, end);
        }

        return null;
    }
}
