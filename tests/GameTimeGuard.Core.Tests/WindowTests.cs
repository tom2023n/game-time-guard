using GameTimeGuard.Core.Schedule;

namespace GameTimeGuard.Core.Tests;

public class WindowTests
{
    [Fact]
    public void Unrestricted_schedule_is_always_open()
    {
        var config = DefaultPolicy.Uninitialized();
        Assert.True(WindowEvaluator.IsOpen(config, DayOfWeek.Monday, new TimeOnly(3, 0)));
    }

    [Fact]
    public void Empty_restricted_day_is_closed()
    {
        var config = Restricted("18:00-21:00");
        config.PlayWindows.Clear();
        Assert.False(WindowEvaluator.IsOpen(config, DayOfWeek.Monday, new TimeOnly(19, 0)));
    }

    [Fact]
    public void Inside_window_is_open_and_end_is_exclusive()
    {
        var config = Restricted("18:00-21:00");
        Assert.False(WindowEvaluator.IsOpen(config, DayOfWeek.Monday, new TimeOnly(17, 59)));
        Assert.True(WindowEvaluator.IsOpen(config, DayOfWeek.Monday, new TimeOnly(18, 0)));
        Assert.True(WindowEvaluator.IsOpen(config, DayOfWeek.Monday, new TimeOnly(20, 59)));
        Assert.False(WindowEvaluator.IsOpen(config, DayOfWeek.Monday, new TimeOnly(21, 0)));
    }

    [Fact]
    public void Multiple_windows_and_overnight_ranges_work()
    {
        var config = Restricted("12:00-13:00, 22:00-02:00");
        Assert.True(WindowEvaluator.IsOpen(config, DayOfWeek.Monday, new TimeOnly(12, 30)));
        Assert.False(WindowEvaluator.IsOpen(config, DayOfWeek.Monday, new TimeOnly(13, 0)));
        Assert.True(WindowEvaluator.IsOpen(config, DayOfWeek.Monday, new TimeOnly(23, 15)));
        Assert.True(WindowEvaluator.IsOpen(config, DayOfWeek.Monday, new TimeOnly(1, 0)));
        Assert.False(WindowEvaluator.IsOpen(config, DayOfWeek.Monday, new TimeOnly(2, 0)));
        Assert.False(WindowEvaluator.IsOpen(config, DayOfWeek.Monday, new TimeOnly(15, 0)));
    }

    [Fact]
    public void Parser_accepts_chinese_separators_and_rejects_garbage()
    {
        Assert.True(WindowText.TryParse("18:00—21:00，12:00-13:00", out var ranges, out var error));
        Assert.Null(error);
        Assert.Equal("18:00-21:00, 12:00-13:00", WindowText.Format(ranges));

        Assert.False(WindowText.TryParse("晚上", out _, out error));
        Assert.Contains("无法识别", error);
        Assert.False(WindowText.TryParse("18:00-18:00", out _, out error));
        Assert.Contains("不能相同", error);
    }

    [Fact]
    public void Defaults_are_sunday_180_and_other_days_120()
    {
        var config = DefaultPolicy.Uninitialized();
        Assert.Equal(180, config.MinutesFor(DayOfWeek.Sunday));
        Assert.Equal(120, config.MinutesFor(DayOfWeek.Wednesday));
        Assert.Equal(["steam", "wegame", "delta-force"], config.EnabledRuleIds);
        Assert.True(config.PlayWindowsUnrestricted);
        Assert.False(config.SetupComplete);
    }

    private static GuardConfig Restricted(string monday)
    {
        var config = DefaultPolicy.Uninitialized();
        config.PlayWindowsUnrestricted = false;
        Assert.True(WindowText.TryParse(monday, out var ranges, out _));
        config.PlayWindows = DayNames.All.Select(day => new DayWindowSet
        {
            Day = day,
            Ranges = day == "Mon" ? ranges : []
        }).ToList();
        return config;
    }
}
