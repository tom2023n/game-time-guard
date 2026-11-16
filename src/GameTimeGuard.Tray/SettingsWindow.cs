using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameTimeGuard.Core.Ipc;
using GameTimeGuard.Core.Schedule;

namespace GameTimeGuard.Tray;

internal sealed class SettingsWindow : Window
{
    private readonly GuardPipe _pipe;
    private string _password;
    private readonly List<CustomGameRule> _custom;
    private readonly Dictionary<string, TextBox> _minutes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBox> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string Id, CheckBox Box)> _rules = [];
    private readonly CheckBox _unrestricted = new() { Content = "全天允许，不限制时段", Margin = new Thickness(0, 0, 0, 8) };
    private readonly ListBox _customList = new() { Height = 140 };
    private readonly TextBlock _today;

    public SettingsWindow(GuardPipe pipe, string password, SettingsSnapshot settings, StatusReport? status, bool administrator)
    {
        _pipe = pipe;
        _password = password;
        _custom = settings.CustomGames.Select(game => new CustomGameRule
        {
            Id = game.Id,
            DisplayName = game.DisplayName,
            Kind = game.Kind,
            Path = game.Path
        }).ToList();
        Title = "游戏时间守护 · 家长设置";
        Theme.Chrome(this, 900, 680);
        Closed += (_, _) => _password = "";

        var tabs = new TabControl { Margin = new Thickness(16) };
        _today = Theme.Title("正在读取…", 28);
        tabs.Items.Add(Page("今日概况", TodayPage(status, administrator)));
        tabs.Items.Add(Page("每日时长", MinutesPage(settings)));
        tabs.Items.Add(Page("允许时段", WindowsPage(settings)));
        tabs.Items.Add(Page("管控范围", RulesPage(settings)));
        tabs.Items.Add(Page("临时加时", BonusPage()));
        tabs.Items.Add(Page("家长密码", PasswordPage()));
        Content = tabs;
        RefreshCustom();
    }

    private UIElement TodayPage(StatusReport? status, bool administrator)
    {
        var panel = new StackPanel();
        if (administrator)
            panel.Children.Add(Theme.CardPanel(Theme.Body(AdminAccount.Warning), Color.FromRgb(246, 226, 184)));
        panel.Children.Add(_today);
        panel.Children.Add(Theme.Body(Describe(status)));
        panel.Children.Add(Theme.Quiet("刷新", () => _ = RefreshAsync()));
        if (status?.TamperLatched == true)
        {
            panel.Children.Add(Theme.Body("解除锁定后，今天已经扣掉的时间不会恢复。如果立刻又被锁定，请先在 Windows 里打开自动设置时间并联网校时。"));
            panel.Children.Add(Theme.Primary("解除锁定", () => _ = AcknowledgeAsync()));
        }

        ApplyStatus(status);
        return panel;
    }

    private UIElement MinutesPage(SettingsSnapshot settings)
    {
        var panel = new StackPanel();
        panel.Children.Add(Theme.Body("0 分钟表示这一天不允许玩游戏。周日默认 180，其他日子默认 120。", muted: true));
        foreach (var day in DayNames.All)
        {
            var box = Theme.Field();
            box.Width = 120;
            box.Text = settings.DailyMinutes.FirstOrDefault(entry => entry.Day.Equals(day, StringComparison.OrdinalIgnoreCase))?.Minutes.ToString() ?? "0";
            _minutes[day] = box;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var label = Theme.Body(DayNames.Chinese(day));
            label.Width = 64;
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            row.Children.Add(box);
            panel.Children.Add(row);
        }

        panel.Children.Add(Theme.Primary("保存时长和时段", () => _ = SaveAsync()));
        return panel;
    }

    private UIElement WindowsPage(SettingsSnapshot settings)
    {
        var panel = new StackPanel();
        _unrestricted.IsChecked = settings.PlayWindowsUnrestricted;
        panel.Children.Add(_unrestricted);
        panel.Children.Add(Theme.Body("每个时段写成 18:00-21:00。多个时段用逗号分开。22:00-07:00 表示跨过午夜。结束时刻不算在内。", muted: true));
        foreach (var day in DayNames.All)
        {
            var box = Theme.Field();
            var found = settings.PlayWindows.FirstOrDefault(entry => entry.Day.Equals(day, StringComparison.OrdinalIgnoreCase));
            box.Text = WindowText.Format(found?.Ranges);
            _windows[day] = box;
            panel.Children.Add(Theme.Body(DayNames.Chinese(day), muted: true));
            panel.Children.Add(box);
        }

        panel.Children.Add(Theme.Primary("保存时长和时段", () => _ = SaveAsync()));
        return panel;
    }

    private UIElement RulesPage(SettingsSnapshot settings)
    {
        var panel = new StackPanel();
        panel.Children.Add(Theme.Body("按安装目录识别，部分平台也会看数字签名。只把 exe 改名不会躲开。微信、QQ、网易云音乐不在管控里。"));
        foreach (var rule in settings.Catalog)
        {
            var box = new CheckBox
            {
                Content = rule.DisplayName + " — " + rule.Description,
                IsChecked = rule.Enabled,
                Margin = new Thickness(0, 6, 0, 6)
            };
            _rules.Add((rule.Id, box));
            panel.Children.Add(box);
        }

        panel.Children.Add(Theme.Body("自定义游戏。优先添加文件夹；只指定某个 exe 时，改名就能躲开。", muted: true));
        panel.Children.Add(_customList);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        buttons.Children.Add(Theme.Quiet("添加文件夹", AddFolder));
        buttons.Children.Add(Theme.Quiet("添加程序", AddExe));
        buttons.Children.Add(Theme.Quiet("移除", RemoveCustom));
        panel.Children.Add(buttons);
        panel.Children.Add(Theme.Primary("保存管控范围", () => _ = SaveAsync()));
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
    }

    private UIElement BonusPage()
    {
        var panel = new StackPanel();
        panel.Children.Add(Theme.Body("临时加时只对今天有效，过了今天的逻辑日期就清零。检测到时间或记录被改动时，要先解除锁定。"));
        var choice = new ComboBox { ItemsSource = new[] { "15 分钟", "30 分钟", "60 分钟" }, SelectedIndex = 1, Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        var password = Theme.Secret();
        panel.Children.Add(choice);
        panel.Children.Add(Theme.Body("再输入一次家长密码", muted: true));
        panel.Children.Add(password);
        panel.Children.Add(Theme.Primary("确认加时", () => _ = BonusAsync(choice.SelectedIndex switch { 0 => 15, 2 => 60, _ => 30 }, password)));
        return panel;
    }

    private UIElement PasswordPage()
    {
        var panel = new StackPanel();
        panel.Children.Add(Theme.Body("忘记密码时没有后门。需要家长用自己的管理员账户，按说明里的「彻底移除」手动处理。"));
        var current = Theme.Secret();
        var next = Theme.Secret();
        var again = Theme.Secret();
        panel.Children.Add(Theme.Body("当前密码", muted: true));
        panel.Children.Add(current);
        panel.Children.Add(Theme.Body("新密码", muted: true));
        panel.Children.Add(next);
        panel.Children.Add(Theme.Body("再输入一次新密码", muted: true));
        panel.Children.Add(again);
        panel.Children.Add(Theme.Primary("修改密码", () => _ = ChangePasswordAsync(current, next, again)));
        return panel;
    }

    private async Task SaveAsync()
    {
        var update = Collect();
        if (update is null)
            return;
        var response = await _pipe.RequestAsync(new IpcRequest
        {
            Op = IpcOps.UpdateSettings,
            Password = _password,
            Settings = update
        });
        if (response is { Ok: true })
        {
            ApplyStatus(response.Status);
            Dialogs.Alert("已保存", "新的时长、时段和管控范围已经生效。");
            return;
        }

        Dialogs.Alert("没有保存", response?.Error ?? "连不上守护服务。");
    }

    private SettingsUpdate? Collect()
    {
        var update = DefaultPolicy.Create();
        update.DailyMinutes.Clear();
        foreach (var day in DayNames.All)
        {
            if (!int.TryParse(_minutes[day].Text.Trim(), out var minutes) || minutes is < 0 or > 1440)
            {
                Dialogs.Alert("时长不对", $"{DayNames.Chinese(day)}的分钟数要在 0 到 1440 之间。");
                return null;
            }

            update.DailyMinutes.Add(new DayQuota { Day = day, Minutes = minutes });
        }

        update.PlayWindowsUnrestricted = _unrestricted.IsChecked == true;
        update.PlayWindows.Clear();
        foreach (var day in DayNames.All)
        {
            if (!WindowText.TryParse(_windows[day].Text, out var ranges, out var error))
            {
                Dialogs.Alert("时段不对", $"{DayNames.Chinese(day)}：{error}");
                return null;
            }

            update.PlayWindows.Add(new DayWindowSet { Day = day, Ranges = ranges });
        }

        update.EnabledRuleIds = _rules.Where(rule => rule.Box.IsChecked == true).Select(rule => rule.Id).ToList();
        update.CustomGames = _custom.ToList();
        update.GraceSeconds = 30;
        return update;
    }

    private async Task BonusAsync(int minutes, PasswordBox password)
    {
        var response = await _pipe.RequestAsync(new IpcRequest
        {
            Op = IpcOps.GrantBonus,
            Password = password.Password,
            Minutes = minutes
        });
        password.Clear();
        if (response is { Ok: true })
        {
            ApplyStatus(response.Status);
            Dialogs.Alert("已加时", $"今天额外增加了 {minutes} 分钟。");
            return;
        }

        Dialogs.Alert("没有加时", response?.Error ?? "连不上守护服务。");
    }

    private async Task ChangePasswordAsync(PasswordBox current, PasswordBox next, PasswordBox again)
    {
        if (next.Password != again.Password)
        {
            Dialogs.Alert("密码不一致", "两次输入的新密码不一样。");
            return;
        }

        var response = await _pipe.RequestAsync(new IpcRequest
        {
            Op = IpcOps.ChangePassword,
            Password = current.Password,
            NewPassword = next.Password
        });
        if (response is { Ok: true })
        {
            _password = next.Password;
            current.Clear();
            next.Clear();
            again.Clear();
            Dialogs.Alert("已修改", "家长密码已经更换。卸载时也要用新密码。");
            return;
        }

        Dialogs.Alert("没有修改", response?.Error ?? "连不上守护服务。");
    }

    private async Task AcknowledgeAsync()
    {
        var response = await _pipe.RequestAsync(new IpcRequest { Op = IpcOps.AcknowledgeTamper, Password = _password });
        if (response is { Ok: true })
        {
            ApplyStatus(response.Status);
            Dialogs.Alert("已解除锁定", "今天的剩余时间不会恢复。如果需要，可以再给一次临时加时。");
            return;
        }

        Dialogs.Alert("没有解除", response?.Error ?? "连不上守护服务。");
    }

    private async Task RefreshAsync()
    {
        var response = await _pipe.RequestAsync(new IpcRequest { Op = IpcOps.Status });
        if (response?.Status is null)
        {
            Dialogs.Alert("没有刷新", "连不上守护服务。");
            return;
        }

        ApplyStatus(response.Status);
    }

    private void ApplyStatus(StatusReport? status) => _today.Text = status is null ? "还没有读到今天的状态" : $"剩余 {Theme.Clock(status.RemainingSeconds)}";

    private static string Describe(StatusReport? status)
    {
        if (status is null)
            return "守护服务还没有连上。";
        var games = status.RunningGames.Length == 0 ? "当前没有识别到受管控的游戏。" : "正在运行：" + string.Join("、", status.RunningGames);
        return $"{Theme.PhaseText(status.Phase)}。今天已用 {Theme.Clock(status.UsedSeconds)}，额度 {Theme.Clock(status.QuotaSeconds)}，临时加时 {Theme.Clock(status.BonusSeconds)}。日期按可信时间计算，是 {status.LogicalDate}。{games}";
    }

    private void AddFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "选择游戏所在的文件夹", UseDescriptionForTitle = true };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            return;
        _custom.Add(new CustomGameRule
        {
            Id = NewId(),
            DisplayName = Path.GetFileName(dialog.SelectedPath.TrimEnd('\\', '/')),
            Kind = "folder",
            Path = dialog.SelectedPath
        });
        RefreshCustom();
    }

    private void AddExe()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "程序|*.exe", Title = "选择游戏程序" };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
            return;
        _custom.Add(new CustomGameRule
        {
            Id = NewId(),
            DisplayName = Path.GetFileNameWithoutExtension(dialog.FileName),
            Kind = "exe",
            Path = dialog.FileName
        });
        RefreshCustom();
    }

    private void RemoveCustom()
    {
        if (_customList.SelectedItem is not string text)
            return;
        var index = _customList.SelectedIndex;
        if (index >= 0 && index < _custom.Count && text.Contains(_custom[index].Path, StringComparison.OrdinalIgnoreCase))
            _custom.RemoveAt(index);
        RefreshCustom();
    }

    private void RefreshCustom()
    {
        _customList.Items.Clear();
        foreach (var game in _custom)
            _customList.Items.Add($"{game.DisplayName}  ({(game.Kind == "exe" ? "程序" : "文件夹")})  {game.Path}");
    }

    private static string NewId() => "custom-" + Guid.NewGuid().ToString("N")[..8];

    private static TabItem Page(string title, UIElement content) => new()
    {
        Header = title,
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8),
            Content = content
        }
    };
}
