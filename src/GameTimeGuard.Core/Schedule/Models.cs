using System.Text.Json.Serialization;

namespace GameTimeGuard.Core.Schedule;

public sealed class DayQuota
{
    [JsonPropertyName("day")]
    public string Day { get; set; } = "";

    [JsonPropertyName("minutes")]
    public int Minutes { get; set; }
}

public sealed class TimeRange
{
    [JsonPropertyName("start")]
    public string Start { get; set; } = "";

    [JsonPropertyName("end")]
    public string End { get; set; } = "";
}

public sealed class DayWindowSet
{
    [JsonPropertyName("day")]
    public string Day { get; set; } = "";

    [JsonPropertyName("ranges")]
    public List<TimeRange> Ranges { get; set; } = [];
}

public sealed class CustomGameRule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    /// <summary>folder 或 exe。目录规则在改名后仍然有效，程序规则只认完整路径。</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "folder";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
}

public sealed class PasswordHash
{
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = "PBKDF2-SHA256";

    [JsonPropertyName("iterations")]
    public int Iterations { get; set; }

    [JsonPropertyName("salt")]
    public string Salt { get; set; } = "";

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";
}

/// <summary>
/// 持久化的家长配置。属性顺序参与签名，请只在末尾追加新属性。
/// </summary>
public sealed class GuardConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("sequence")]
    public long Sequence { get; set; }

    [JsonPropertyName("setupComplete")]
    public bool SetupComplete { get; set; }

    [JsonPropertyName("parentPassword")]
    public PasswordHash? ParentPassword { get; set; }

    [JsonPropertyName("dailyMinutes")]
    public List<DayQuota> DailyMinutes { get; set; } = [];

    [JsonPropertyName("playWindowsUnrestricted")]
    public bool PlayWindowsUnrestricted { get; set; } = true;

    [JsonPropertyName("playWindows")]
    public List<DayWindowSet> PlayWindows { get; set; } = [];

    [JsonPropertyName("enabledRuleIds")]
    public List<string> EnabledRuleIds { get; set; } = [];

    [JsonPropertyName("customGames")]
    public List<CustomGameRule> CustomGames { get; set; } = [];

    [JsonPropertyName("graceSeconds")]
    public int GraceSeconds { get; set; } = 30;

    [JsonPropertyName("lastUsageSequence")]
    public long LastUsageSequence { get; set; }

    public int MinutesFor(DayOfWeek day)
    {
        var key = DayNames.From(day);
        foreach (var entry in DailyMinutes)
        {
            if (entry.Day.Equals(key, StringComparison.OrdinalIgnoreCase))
                return entry.Minutes;
        }
        return 0;
    }

    public GuardConfig Clone()
    {
        var copy = GuardJson.Deserialize<GuardConfig>(GuardJson.Serialize(this));
        return copy ?? new GuardConfig();
    }
}

public sealed class SettingsUpdate
{
    [JsonPropertyName("dailyMinutes")]
    public List<DayQuota> DailyMinutes { get; set; } = [];

    [JsonPropertyName("playWindowsUnrestricted")]
    public bool PlayWindowsUnrestricted { get; set; } = true;

    [JsonPropertyName("playWindows")]
    public List<DayWindowSet> PlayWindows { get; set; } = [];

    [JsonPropertyName("enabledRuleIds")]
    public List<string> EnabledRuleIds { get; set; } = [];

    [JsonPropertyName("customGames")]
    public List<CustomGameRule> CustomGames { get; set; } = [];

    [JsonPropertyName("graceSeconds")]
    public int GraceSeconds { get; set; } = 30;
}
