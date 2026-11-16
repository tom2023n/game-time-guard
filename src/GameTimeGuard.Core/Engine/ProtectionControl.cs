using System.Globalization;
using GameTimeGuard.Core.Schedule;

namespace GameTimeGuard.Core.Engine;

/// <summary>
/// 家长密码控制的保护开关。暂停和停止都记在已签名的配置里，到点或手动恢复之前不计时、不关游戏。
/// </summary>
public static class ProtectionControl
{
    public const string Enforcing = "enforcing";
    public const string Paused = "paused";
    public const string Stopped = "stopped";
    public static readonly int[] PauseChoices = [15, 30, 60, 120];

    public static bool IsIdle(GuardConfig config, DateTimeOffset trustedUtc)
    {
        if (config.ProtectionState.Equals(Stopped, StringComparison.OrdinalIgnoreCase))
            return true;
        return config.ProtectionState.Equals(Paused, StringComparison.OrdinalIgnoreCase)
            && TryParse(config.PauseUntilUtc, out var until)
            && trustedUtc < until;
    }

    public static GuardConfig Normalize(GuardConfig config, DateTimeOffset trustedUtc)
    {
        if (!config.ProtectionState.Equals(Paused, StringComparison.OrdinalIgnoreCase))
            return config;
        if (TryParse(config.PauseUntilUtc, out var until) && trustedUtc < until)
            return config;
        config.ProtectionState = Enforcing;
        config.PauseUntilUtc = null;
        config.TraySuppressed = false;
        return config;
    }

    public static string? Pause(GuardConfig config, DateTimeOffset trustedUtc, int minutes)
    {
        if (Array.IndexOf(PauseChoices, minutes) < 0)
            return "只能暂停 15、30、60 或 120 分钟";
        config.ProtectionState = Paused;
        config.PauseUntilUtc = trustedUtc.ToUniversalTime().AddMinutes(minutes).ToString("O", CultureInfo.InvariantCulture);
        config.Sequence++;
        return null;
    }

    public static void Stop(GuardConfig config, bool suppressTray)
    {
        config.ProtectionState = Stopped;
        config.PauseUntilUtc = null;
        config.TraySuppressed = suppressTray;
        config.Sequence++;
    }

    public static void Resume(GuardConfig config)
    {
        config.ProtectionState = Enforcing;
        config.PauseUntilUtc = null;
        config.TraySuppressed = false;
        config.Sequence++;
    }

    public static void ArmUninstall(GuardConfig config, DateTimeOffset trustedUtc, TimeSpan window)
    {
        config.UninstallArmedUntilUtc = trustedUtc.ToUniversalTime().Add(window).ToString("O", CultureInfo.InvariantCulture);
        config.Sequence++;
    }

    public static bool UninstallArmed(GuardConfig config, DateTimeOffset trustedUtc)
        => TryParse(config.UninstallArmedUntilUtc, out var until) && trustedUtc < until;

    public static bool TryParse(string? text, out DateTimeOffset value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = default;
            return false;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }
}
