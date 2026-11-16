namespace GameTimeGuard.Core.Schedule;

public static class DefaultPolicy
{
    public static readonly string[] InitiallyEnabled = ["steam", "wegame", "delta-force"];

    public static SettingsUpdate Create()
    {
        return new SettingsUpdate
        {
            DailyMinutes = DayNames.All.Select(day => new DayQuota
            {
                Day = day,
                Minutes = day == "Sun" ? 180 : 120
            }).ToList(),
            PlayWindowsUnrestricted = true,
            PlayWindows = DayNames.All.Select(day => new DayWindowSet { Day = day, Ranges = [] }).ToList(),
            EnabledRuleIds = [.. InitiallyEnabled],
            CustomGames = [],
            GraceSeconds = 30
        };
    }

    public static GuardConfig Uninitialized()
    {
        var config = Apply(new GuardConfig(), Create());
        config.SetupComplete = false;
        config.ParentPassword = null;
        config.Sequence = 0;
        config.LastUsageSequence = 0;
        return config;
    }

    public static GuardConfig EmergencyLock()
    {
        return new GuardConfig
        {
            Version = 1,
            Sequence = 1,
            SetupComplete = true,
            ParentPassword = null,
            DailyMinutes = DayNames.All.Select(day => new DayQuota { Day = day, Minutes = 0 }).ToList(),
            PlayWindowsUnrestricted = false,
            PlayWindows = DayNames.All.Select(day => new DayWindowSet { Day = day, Ranges = [] }).ToList(),
            EnabledRuleIds =
            [
                "steam", "wegame", "epic", "battlenet", "netease", "mihoyo", "delta-force", "tencent"
            ],
            CustomGames = [],
            GraceSeconds = 30,
            LastUsageSequence = 0
        };
    }

    public static GuardConfig Apply(GuardConfig config, SettingsUpdate update)
    {
        config.DailyMinutes = update.DailyMinutes.Select(entry => new DayQuota { Day = entry.Day, Minutes = entry.Minutes }).ToList();
        config.PlayWindowsUnrestricted = update.PlayWindowsUnrestricted;
        config.PlayWindows = update.PlayWindows.Select(day => new DayWindowSet
        {
            Day = day.Day,
            Ranges = day.Ranges.Select(range => new TimeRange { Start = range.Start, End = range.End }).ToList()
        }).ToList();
        config.EnabledRuleIds = update.EnabledRuleIds.Select(id => id.Trim()).Where(id => id.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        config.CustomGames = update.CustomGames.Select(game => new CustomGameRule
        {
            Id = game.Id.Trim(),
            DisplayName = game.DisplayName.Trim(),
            Kind = game.Kind,
            Path = WindowsPath.Normalize(game.Path) ?? ""
        }).ToList();
        config.GraceSeconds = update.GraceSeconds;
        return config;
    }

    public static SettingsUpdate FromConfig(GuardConfig config)
    {
        var copy = config.Clone();
        return new SettingsUpdate
        {
            DailyMinutes = copy.DailyMinutes,
            PlayWindowsUnrestricted = copy.PlayWindowsUnrestricted,
            PlayWindows = copy.PlayWindows,
            EnabledRuleIds = copy.EnabledRuleIds,
            CustomGames = copy.CustomGames,
            GraceSeconds = copy.GraceSeconds
        };
    }
}
