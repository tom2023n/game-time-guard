using GameTimeGuard.Core.Rules;
using GameTimeGuard.Core.Schedule;

namespace GameTimeGuard.Core.Tests;

public class RuleMatcherTests
{
    private readonly PlatformCatalog _catalog = PlatformCatalog.LoadEmbedded();

    [Fact]
    public void Embedded_rules_cover_the_built_in_platforms()
    {
        var ids = _catalog.Rules.Select(rule => rule.Id).ToArray();
        Assert.Contains("steam", ids);
        Assert.Contains("wegame", ids);
        Assert.Contains("epic", ids);
        Assert.Contains("battlenet", ids);
        Assert.Contains("netease", ids);
        Assert.Contains("mihoyo", ids);
        Assert.Contains("delta-force", ids);
        Assert.Contains("tencent", ids);
        Assert.Equal(DefaultPolicy.InitiallyEnabled, _catalog.Rules.Where(rule => rule.EnabledByDefault).Select(rule => rule.Id).ToArray());
    }

    [Theory]
    [InlineData(@"D:\Steam\steamapps\common\ELDEN RING\renamed.exe", "steam")]
    [InlineData(@"D:\Steam\steamapps\common\Delta Force\Binaries\DeltaForceClient.exe", "steam")]
    [InlineData(@"C:\WeGame\wegame.exe", "wegame")]
    [InlineData(@"E:\WeGameApps\rail_apps\Game\game.exe", "wegame")]
    [InlineData(@"D:\Delta Force\Launcher\DeltaForceLauncher.exe", "delta-force")]
    [InlineData(@"D:\三角洲行动\start.exe", "delta-force")]
    [InlineData(@"D:\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe", "epic")]
    [InlineData(@"D:\Genshin Impact\GenshinImpact.exe", "mihoyo")]
    [InlineData(@"D:\miHoYo\原神\YuanShen.exe", "mihoyo")]
    [InlineData(@"D:\Star Rail\StarRail.exe", "mihoyo")]
    [InlineData(@"D:\ZenlessZoneZero\ZenlessZoneZero.exe", "mihoyo")]
    [InlineData(@"D:\NetEase\永劫无间\Naraka.exe", "netease")]
    [InlineData(@"D:\英雄联盟\Game\League of Legends.exe", "tencent")]
    [InlineData(@"D:\VALORANT\live\VALORANT.exe", "tencent")]
    public void Known_install_paths_match_even_if_the_exe_was_renamed(string path, string ruleId)
    {
        var matches = Match(path, publisher: null);
        Assert.Contains(ruleId, matches);
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe")]
    [InlineData(@"C:\Windows\System32\notepad.exe")]
    [InlineData(@"D:\notgames\renamed.exe")]
    [InlineData(@"C:\Program Files\Tencent\WeChat\WeChat.exe")]
    [InlineData(@"C:\Program Files\Tencent\QQ\Bin\QQ.exe")]
    [InlineData(@"C:\Program Files\NetEase\CloudMusic\cloudmusic.exe")]
    [InlineData(@"C:\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe")]
    public void Unrelated_or_excluded_programs_do_not_match(string path)
    {
        Assert.Empty(Match(path, publisher: "Tencent"));
    }

    [Fact]
    public void Publisher_alone_matches_mihoyo_outside_the_usual_folder()
    {
        var matches = Match(@"E:\Games\copied\YuanShen.exe", "miHoYo Co.,Ltd.");
        Assert.Contains("mihoyo", matches);
    }

    [Fact]
    public void Epic_publisher_matches_a_custom_folder_but_not_the_launcher()
    {
        Assert.Contains("epic", Match(@"E:\Games\Fortnite\Binaries\FortniteClient.exe", "Epic Games, Inc."));
        Assert.DoesNotContain("epic", Match(@"C:\Epic Games\Launcher\Engine\Binaries\EpicGamesLauncher.exe", "Epic Games, Inc."));
    }

    [Fact]
    public void Tencent_publisher_without_a_game_path_does_not_match()
    {
        Assert.Empty(Match(@"D:\Apps\Something\helper.exe", "Tencent Technology (Shenzhen) Company Limited"));
    }

    [Fact]
    public void Riot_publisher_matches_and_vanguard_is_excluded()
    {
        Assert.Contains("tencent", Match(@"E:\Custom\Shooter\VALORANT-Win64-Shipping.exe", "Riot Games, Inc."));
        Assert.DoesNotContain("tencent", Match(@"C:\Program Files\Riot Vanguard\vgc.exe", "Riot Games, Inc."));
    }

    [Fact]
    public void Custom_folder_survives_an_exe_rename_and_exe_rule_does_not()
    {
        var folder = new CustomGameRule
        {
            Id = "folder",
            DisplayName = "自制游戏",
            Kind = "folder",
            Path = @"D:\Kids\MyGame"
        };
        var exe = new CustomGameRule
        {
            Id = "exe",
            DisplayName = "某个程序",
            Kind = "exe",
            Path = @"D:\Kids\Other\game.exe"
        };

        var renamed = Observe(@"D:\Kids\MyGame\bin\renamed.exe");
        var moved = Observe(@"D:\Kids\Other\renamed.exe");
        var exact = Observe(@"D:\Kids\Other\game.exe");
        var sibling = Observe(@"D:\Kids\MyGame2\game.exe");
        var matches = RuleMatcher.MatchAll([renamed, moved, exact, sibling], [], [folder, exe]);

        Assert.Contains(matches, match => match.Process.ExecutablePath == renamed.ExecutablePath && match.RuleIds.Contains("custom:folder"));
        Assert.DoesNotContain(matches, match => match.Process.ExecutablePath == moved.ExecutablePath);
        Assert.Contains(matches, match => match.Process.ExecutablePath == exact.ExecutablePath && match.RuleIds.Contains("custom:exe"));
        Assert.DoesNotContain(matches, match => match.Process.ExecutablePath == sibling.ExecutablePath);
    }

    [Fact]
    public void Publisher_lookup_is_skipped_when_the_path_already_matches()
    {
        var rules = _catalog.Enabled(["steam", "mihoyo", "epic"]);
        Assert.False(RuleMatcher.RequiresPublisherLookup(rules, @"D:\Steam\steamapps\common\Demo\a.exe"));
        Assert.True(RuleMatcher.RequiresPublisherLookup(rules, @"E:\Games\copied\YuanShen.exe"));
        Assert.False(RuleMatcher.RequiresPublisherLookup(_catalog.Enabled(["epic"]), @"C:\Epic Games\Launcher\Portal\Binaries\launcher.exe"));
        Assert.False(RuleMatcher.RequiresPublisherLookup(rules, @"C:\Windows\notepad.exe"));
    }

    [Fact]
    public void System_paths_are_never_matched()
    {
        var custom = new CustomGameRule { Id = "bad", DisplayName = "系统", Kind = "folder", Path = @"C:\Windows" };
        var matches = RuleMatcher.MatchAll([Observe(@"C:\Windows\System32\cmd.exe")], _catalog.Rules, [custom]);
        Assert.Empty(matches);
    }

    private IReadOnlyList<string> Match(string path, string? publisher)
    {
        var found = RuleMatcher.MatchAll([Observe(path, publisher)], _catalog.Rules, []);
        return found.Count == 0 ? [] : found[0].RuleIds.ToArray();
    }

    private static ProcessObservation Observe(string path, string? publisher = null)
        => new(100, "renamed", path, publisher);
}
