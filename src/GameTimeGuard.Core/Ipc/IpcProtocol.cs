using System.Text.Json.Serialization;
using GameTimeGuard.Core;
using GameTimeGuard.Core.Schedule;
using GameTimeGuard.Core.Security;

namespace GameTimeGuard.Core.Ipc;

public static class IpcOps
{
    public const string Status = "status";
    public const string GetSettings = "getSettings";
    public const string Setup = "setup";
    public const string UpdateSettings = "updateSettings";
    public const string ChangePassword = "changePassword";
    public const string GrantBonus = "grantBonus";
    public const string AcknowledgeTamper = "acknowledgeTamper";
    public const string AuthorizeUninstall = "authorizeUninstall";
    public const string PauseProtection = "pauseProtection";
    public const string StopProtection = "stopProtection";
    public const string ResumeProtection = "resumeProtection";
    public const string ExitProgram = "exitProgram";
    public const string AcknowledgeNotices = "acknowledgeNotices";
    public const string TestEmail = "testEmail";
}

public sealed class StatusReport
{
    [JsonPropertyName("setupComplete")]
    public bool SetupComplete { get; set; }

    [JsonPropertyName("serviceOnline")]
    public bool ServiceOnline { get; set; }

    [JsonPropertyName("logicalDate")]
    public string LogicalDate { get; set; } = "";

    [JsonPropertyName("usedSeconds")]
    public int UsedSeconds { get; set; }

    [JsonPropertyName("quotaSeconds")]
    public int QuotaSeconds { get; set; }

    [JsonPropertyName("bonusSeconds")]
    public int BonusSeconds { get; set; }

    [JsonPropertyName("remainingSeconds")]
    public int RemainingSeconds { get; set; }

    [JsonPropertyName("gameRunning")]
    public bool GameRunning { get; set; }

    [JsonPropertyName("runningGames")]
    public string[] RunningGames { get; set; } = [];

    [JsonPropertyName("withinWindow")]
    public bool WithinWindow { get; set; }

    [JsonPropertyName("tamperLatched")]
    public bool TamperLatched { get; set; }

    [JsonPropertyName("tamperReason")]
    public string? TamperReason { get; set; }

    [JsonPropertyName("phase")]
    public string Phase { get; set; } = "";

    [JsonPropertyName("graceSecondsRemaining")]
    public int GraceSecondsRemaining { get; set; }

    [JsonPropertyName("reasonCode")]
    public string ReasonCode { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = AppVersion.Current;

    [JsonPropertyName("protectionState")]
    public string ProtectionState { get; set; } = "enforcing";

    [JsonPropertyName("pauseUntilUtc")]
    public string PauseUntilUtc { get; set; } = "";

    [JsonPropertyName("traySuppressed")]
    public bool TraySuppressed { get; set; }

    [JsonPropertyName("pendingNotices")]
    public List<TamperNotice> PendingNotices { get; set; } = [];
}

public sealed class TamperNotice
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("utc")]
    public string Utc { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public sealed class RuleInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

public sealed class SettingsSnapshot
{
    [JsonPropertyName("dailyMinutes")]
    public List<DayQuota> DailyMinutes { get; set; } = [];

    [JsonPropertyName("playWindowsUnrestricted")]
    public bool PlayWindowsUnrestricted { get; set; }

    [JsonPropertyName("playWindows")]
    public List<DayWindowSet> PlayWindows { get; set; } = [];

    [JsonPropertyName("enabledRuleIds")]
    public List<string> EnabledRuleIds { get; set; } = [];

    [JsonPropertyName("customGames")]
    public List<CustomGameRule> CustomGames { get; set; } = [];

    [JsonPropertyName("graceSeconds")]
    public int GraceSeconds { get; set; }

    [JsonPropertyName("gapPolicy")]
    public string GapPolicy { get; set; } = "penalty";

    [JsonPropertyName("gapPenaltyMinutes")]
    public int GapPenaltyMinutes { get; set; } = 30;

    [JsonPropertyName("emailEnabled")]
    public bool EmailEnabled { get; set; }

    [JsonPropertyName("smtpHost")]
    public string SmtpHost { get; set; } = "";

    [JsonPropertyName("smtpPort")]
    public int SmtpPort { get; set; } = 587;

    [JsonPropertyName("smtpUseSsl")]
    public bool SmtpUseSsl { get; set; } = true;

    [JsonPropertyName("smtpUser")]
    public string SmtpUser { get; set; } = "";

    [JsonPropertyName("notifyAddress")]
    public string NotifyAddress { get; set; } = "";

    [JsonPropertyName("notifyTamper")]
    public bool NotifyTamper { get; set; } = true;

    [JsonPropertyName("notifyTimeUp")]
    public bool NotifyTimeUp { get; set; } = true;

    [JsonPropertyName("notifyDailySummary")]
    public bool NotifyDailySummary { get; set; } = true;

    [JsonPropertyName("hasSmtpPassword")]
    public bool HasSmtpPassword { get; set; }

    [JsonPropertyName("catalog")]
    public List<RuleInfo> Catalog { get; set; } = [];
}

public sealed class IpcRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "request";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("op")]
    public string Op { get; set; } = "";

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("newPassword")]
    public string? NewPassword { get; set; }

    [JsonPropertyName("minutes")]
    public int? Minutes { get; set; }

    [JsonPropertyName("settings")]
    public SettingsUpdate? Settings { get; set; }
}

public sealed class IpcResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "response";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("status")]
    public StatusReport? Status { get; set; }

    [JsonPropertyName("settings")]
    public SettingsSnapshot? Settings { get; set; }
}

public sealed class IpcEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "event";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("minutes")]
    public int? Minutes { get; set; }

    [JsonPropertyName("seconds")]
    public int? Seconds { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public static class IpcProtocol
{
    public static string WriteRequest(IpcRequest request) => GuardJson.Serialize(request);
    public static string WriteResponse(IpcResponse response) => GuardJson.Serialize(response);
    public static string WriteEvent(IpcEvent evt) => GuardJson.Serialize(evt);

    public static bool TryRead(string? line, out IpcRequest? request, out IpcResponse? response, out IpcEvent? evt)
    {
        request = null;
        response = null;
        evt = null;
        if (string.IsNullOrWhiteSpace(line) || line.Length > 65_536)
            return false;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("type", out var typeProperty))
                return false;
            var type = typeProperty.GetString();
            switch (type)
            {
                case "request":
                    request = GuardJson.Deserialize<IpcRequest>(line);
                    return request is not null && !string.IsNullOrWhiteSpace(request.Op);
                case "response":
                    response = GuardJson.Deserialize<IpcResponse>(line);
                    return response is not null;
                case "event":
                    evt = GuardJson.Deserialize<IpcEvent>(line);
                    return evt is not null && !string.IsNullOrWhiteSpace(evt.Name);
                default:
                    return false;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
