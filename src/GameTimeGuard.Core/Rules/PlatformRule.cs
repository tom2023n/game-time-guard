using System.Text.Json.Serialization;

namespace GameTimeGuard.Core.Rules;

public sealed class PlatformRule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("enabledByDefault")]
    public bool EnabledByDefault { get; set; }

    [JsonPropertyName("pathContainsAny")]
    public List<string> PathContainsAny { get; set; } = [];

    [JsonPropertyName("pathNotContainsAny")]
    public List<string> PathNotContainsAny { get; set; } = [];

    [JsonPropertyName("publishers")]
    public List<string> Publishers { get; set; } = [];

    /// <summary>
    /// 为 true 时，数字签名发行方命中即可，即使安装目录被改过。
    /// 发行方覆盖面很广的平台（腾讯、网易）必须为 false，只靠目录匹配。
    /// </summary>
    [JsonPropertyName("publisherAlone")]
    public bool PublisherAlone { get; set; }

    [JsonPropertyName("publisherPathNotContainsAny")]
    public List<string> PublisherPathNotContainsAny { get; set; } = [];
}

public sealed class PlatformRuleFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("rules")]
    public List<PlatformRule> Rules { get; set; } = [];
}

public sealed record ProcessObservation(int ProcessId, string ProcessName, string? ExecutablePath, string? Publisher);

public sealed record ProcessMatch(ProcessObservation Process, IReadOnlyList<string> RuleIds)
{
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Process.ProcessName))
                return Process.ProcessName;
            if (!string.IsNullOrWhiteSpace(Process.ExecutablePath))
                return Path.GetFileNameWithoutExtension(Process.ExecutablePath);
            return $"PID {Process.ProcessId}";
        }
    }
}
