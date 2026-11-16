namespace GameTimeGuard.Core.Schedule;

public static class DayNames
{
    public static readonly string[] All = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    public static string From(DayOfWeek day) => All[(int)day];

    public static bool TryParse(string? text, out DayOfWeek day)
    {
        day = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        for (var i = 0; i < All.Length; i++)
        {
            if (All[i].Equals(text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                day = (DayOfWeek)i;
                return true;
            }
        }
        return false;
    }

    public static string Chinese(string day) => day.ToLowerInvariant() switch
    {
        "sun" => "周日",
        "mon" => "周一",
        "tue" => "周二",
        "wed" => "周三",
        "thu" => "周四",
        "fri" => "周五",
        "sat" => "周六",
        _ => day
    };
}
