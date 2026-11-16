namespace GameTimeGuard.Core.Schedule;

public static class ConfigValidator
{
    public static IReadOnlyList<string> Validate(GuardConfig config) => Validate(
        config.DailyMinutes,
        config.PlayWindows,
        config.CustomGames,
        config.GraceSeconds,
        config.EnabledRuleIds);

    public static IReadOnlyList<string> Validate(SettingsUpdate update)
    {
        var errors = Validate(
            update.DailyMinutes,
            update.PlayWindows,
            update.CustomGames,
            update.GraceSeconds,
            update.EnabledRuleIds).ToList();
        if (update.GapPenaltyMinutes is < 0 or > 24 * 60)
            errors.Add("中断罚时必须在 0 到 1440 分钟之间");
        if (update.GapPolicy is not ("penalty" or "full"))
            errors.Add("中断记账方式只能是罚时或按整段计算");
        if (update.EmailEnabled)
        {
            if (string.IsNullOrWhiteSpace(update.SmtpHost))
                errors.Add("要发信就得填写 SMTP 服务器");
            if (update.SmtpPort is < 1 or > 65535)
                errors.Add("SMTP 端口不正确");
            if (string.IsNullOrWhiteSpace(update.NotifyAddress) || !update.NotifyAddress.Contains('@'))
                errors.Add("收件邮箱看起来不正确");
        }

        return errors;
    }

    private static IReadOnlyList<string> Validate(
        IReadOnlyList<DayQuota> dailyMinutes,
        IReadOnlyList<DayWindowSet> windows,
        IReadOnlyList<CustomGameRule> customGames,
        int graceSeconds,
        IReadOnlyList<string> enabledRuleIds)
    {
        var errors = new List<string>();
        if (graceSeconds is < 0 or > 300)
            errors.Add("关闭前的宽限时间必须在 0 到 300 秒之间");

        var seenDays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in dailyMinutes)
        {
            if (!DayNames.TryParse(entry.Day, out _))
            {
                errors.Add($"未知的星期：{entry.Day}");
                continue;
            }

            if (!seenDays.Add(entry.Day))
                errors.Add($"{DayNames.Chinese(entry.Day)} 的时长重复了");
            if (entry.Minutes is < 0 or > 24 * 60)
                errors.Add($"{DayNames.Chinese(entry.Day)} 的分钟数必须在 0 到 1440 之间");
        }

        foreach (var day in DayNames.All)
        {
            if (!seenDays.Contains(day))
                errors.Add($"缺少{DayNames.Chinese(day)}的每日时长");
        }

        foreach (var day in windows)
        {
            if (!DayNames.TryParse(day.Day, out _))
            {
                errors.Add($"未知的星期：{day.Day}");
                continue;
            }

            if (day.Ranges.Count > 8)
                errors.Add($"{DayNames.Chinese(day.Day)}的时段超过 8 个");
            foreach (var range in day.Ranges)
            {
                if (!WindowEvaluator.TryParse(range.Start, out var start) ||
                    !WindowEvaluator.TryParse(range.End, out var end))
                {
                    errors.Add($"{DayNames.Chinese(day.Day)}有无法识别的时段");
                    continue;
                }

                if (start == end)
                    errors.Add($"{DayNames.Chinese(day.Day)}的时段开始和结束不能相同");
            }
        }

        var seenRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in enabledRuleIds)
        {
            if (string.IsNullOrWhiteSpace(id))
                errors.Add("平台编号不能为空");
            else if (!seenRules.Add(id.Trim()))
                errors.Add($"平台 {id} 重复启用");
        }

        var seenCustom = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in customGames)
        {
            if (string.IsNullOrWhiteSpace(game.Id))
                errors.Add("自定义游戏缺少编号");
            else if (!seenCustom.Add(game.Id))
                errors.Add($"自定义游戏编号重复：{game.Id}");

            if (game.Kind is not ("folder" or "exe"))
                errors.Add($"自定义游戏「{game.DisplayName}」的类型必须是文件夹或程序");

            if (!WindowsPath.IsAbsolute(game.Path))
                errors.Add($"自定义游戏「{game.DisplayName}」需要完整的 Windows 路径");

            if (WindowsPath.IsUnsafe(game.Path))
                errors.Add($"不能把 Windows 系统目录加入管控：{game.Path}");
        }

        return errors;
    }
}

public static class WindowsPath
{
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return path.Trim().Replace('/', '\\');
    }

    public static bool IsAbsolute(string? path)
    {
        var normalized = Normalize(path);
        if (normalized is null)
            return false;
        if (normalized.StartsWith(@"\\", StringComparison.Ordinal))
            return normalized.Length > 2;
        return normalized.Length >= 3
            && char.IsLetter(normalized[0])
            && normalized[1] == ':'
            && normalized[2] == '\\';
    }

    public static bool IsUnsafe(string? path)
    {
        var normalized = Normalize(path);
        if (normalized is null)
            return true;
        if (normalized.Contains("\\windows\\", StringComparison.OrdinalIgnoreCase))
            return true;
        var file = System.IO.Path.GetFileName(normalized);
        return file.Equals("GameTimeGuard.Service.exe", StringComparison.OrdinalIgnoreCase)
            || file.Equals("GameTimeGuard.Tray.exe", StringComparison.OrdinalIgnoreCase);
    }
}
