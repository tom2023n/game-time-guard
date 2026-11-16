using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Threading;
using GameTimeGuard.Core.Ipc;

namespace GameTimeGuard.Tray;

internal sealed class TrayShell : IDisposable
{
    private readonly GuardPipe _pipe = new();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _remaining;
    private readonly DispatcherTimer _timer;
    private bool _wizardOffered;
    private bool _adminWarned;
    private bool _noticesShown;
    private bool _closed;
    private StatusReport? _status;

    public TrayShell()
    {
        _remaining = new ToolStripMenuItem("正在连接守护服务…") { Enabled = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_remaining);
        menu.Items.Add("打开设置", null, (_, _) => OpenSettings());
        menu.Items.Add("申请临时加时", null, (_, _) => OpenBonus());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("暂停 30 分钟", null, (_, _) => _ = ChangeProtection(IpcOps.PauseProtection, 30));
        menu.Items.Add("停止保护", null, (_, _) => _ = ChangeProtection(IpcOps.StopProtection, null));
        menu.Items.Add("恢复保护", null, (_, _) => _ = ChangeProtection(IpcOps.ResumeProtection, null));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("没有家长密码就不能退出") { Enabled = false });
        menu.Items.Add("退出程序", null, (_, _) => _ = ExitWithPassword());

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = "游戏时间守护",
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => OpenSettings();
        _pipe.EventReceived += evt => System.Windows.Application.Current.Dispatcher.Invoke(() => OnEvent(evt));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    public void Start(bool wizardRequested)
    {
        if (wizardRequested)
            _wizardOffered = false;
        _timer.Start();
        _ = RefreshAsync();
    }

    public void Dispose()
    {
        _closed = true;
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        _pipe.Dispose();
    }

    private async Task RefreshAsync()
    {
        if (_closed)
            return;
        var response = await _pipe.RequestAsync(new IpcRequest { Op = IpcOps.Status }).ConfigureAwait(true);
        if (_closed)
            return;
        _status = response?.Status;
        var text = _status is null
            ? "守护服务未连接"
            : _status.SetupComplete
                ? $"今日剩余 {Theme.Clock(_status.RemainingSeconds)}"
                : "等待家长完成初始设置";
        _remaining.Text = text;
        _icon.Text = text.Length > 60 ? text[..60] : text;
        if (_status is { SetupComplete: false })
            OfferWizard();
        else if (_status?.PendingNotices.Count > 0 && !_noticesShown)
            ShowNotices();
        else if (_status is { SetupComplete: true } && !_adminWarned && AdminAccount.IsAdministrator())
        {
            _adminWarned = true;
            Dialogs.ShowNotice("请检查孩子的账户", AdminAccount.Warning);
        }
    }

    private void OfferWizard()
    {
        if (_wizardOffered || _closed)
            return;
        _wizardOffered = true;
        var wizard = new WizardWindow(_pipe) { Topmost = true };
        wizard.ShowDialog();
    }

    private void OnEvent(IpcEvent evt)
    {
        switch (evt.Name)
        {
            case "warn":
                Dialogs.ShowNotice(evt.Minutes == 5 ? "还剩 5 分钟" : "还剩 10 分钟", evt.Message ?? "今天的游戏时间快用完了。");
                break;
            case "grace":
                Dialogs.ShowNotice("即将关闭游戏", evt.Message ?? "宽限时间结束后会关闭游戏。", evt.Seconds ?? 30);
                break;
            case "timeup":
                Dialogs.ShowNotice("游戏时间已到", evt.Message ?? "今天的游戏时间已经用完。");
                break;
            case "tamper":
                Dialogs.ShowNotice("时间或记录异常", evt.Message ?? "检测到系统时间或记录被改动，今天的额度已锁定。");
                break;
        }
    }

    private void OpenSettings()
    {
        _ = OpenSettingsAsync();
    }

    private async Task OpenSettingsAsync()
    {
        var password = Dialogs.AskPassword("打开家长设置需要密码。");
        if (string.IsNullOrEmpty(password))
            return;
        var response = await _pipe.RequestAsync(new IpcRequest { Op = IpcOps.GetSettings, Password = password });
        if (response is not { Ok: true, Settings: not null })
        {
            Dialogs.Alert("没有打开", response?.Error ?? "连不上守护服务。");
            return;
        }

        var window = new SettingsWindow(_pipe, password, response.Settings, response.Status ?? _status, AdminAccount.IsAdministrator());
        window.ShowDialog();
    }

    private void OpenBonus()
    {
        var password = Dialogs.AskPassword("临时加时需要家长密码。加时只在今天有效。");
        if (string.IsNullOrEmpty(password))
            return;
        var choice = BonusChoice();
        if (choice is null)
            return;
        _ = GrantAsync(password, choice.Value);
    }

    private static int? BonusChoice()
    {
        var window = new System.Windows.Window { Title = "临时加时" };
        Theme.Chrome(window, 420, 230);
        int? selected = null;
        var combo = new System.Windows.Controls.ComboBox
        {
            ItemsSource = new[] { "15 分钟", "30 分钟", "60 分钟" },
            SelectedIndex = 1,
            Margin = new System.Windows.Thickness(24, 24, 24, 12)
        };
        var ok = Theme.Primary("确认", () =>
        {
            selected = combo.SelectedIndex switch { 0 => 15, 2 => 60, _ => 30 };
            window.DialogResult = true;
        });
        var panel = new System.Windows.Controls.StackPanel();
        panel.Children.Add(Theme.Body("选择今天要增加的时间。"));
        panel.Children.Add(combo);
        panel.Children.Add(ok);
        window.Content = panel;
        return window.ShowDialog() == true ? selected : null;
    }

    private async Task GrantAsync(string password, int minutes)
    {
        var response = await _pipe.RequestAsync(new IpcRequest { Op = IpcOps.GrantBonus, Password = password, Minutes = minutes });
        if (response is { Ok: true })
            Dialogs.Alert("已加时", $"今天额外增加了 {minutes} 分钟。");
        else
            Dialogs.Alert("没有加时", response?.Error ?? "连不上守护服务。");
    }

    private async Task ChangeProtection(string op, int? minutes)
    {
        var password = Dialogs.AskPassword(op == IpcOps.ResumeProtection ? "恢复保护需要家长密码。" : "暂停或停止保护需要家长密码。");
        if (string.IsNullOrEmpty(password))
            return;
        var response = await _pipe.RequestAsync(new IpcRequest { Op = op, Password = password, Minutes = minutes });
        Dialogs.Alert(response is { Ok: true } ? "已更新" : "没有更新", response is { Ok: true } ? "保护状态已经改好。" : response?.Error ?? "连不上守护服务。");
    }

    private async Task ExitWithPassword()
    {
        var password = Dialogs.AskPassword("退出程序需要家长密码。退出后不再限制游戏，直到你从开始菜单重新打开并恢复保护。");
        if (string.IsNullOrEmpty(password))
            return;
        var response = await _pipe.RequestAsync(new IpcRequest { Op = IpcOps.ExitProgram, Password = password });
        if (response is not { Ok: true })
        {
            Dialogs.Alert("没有退出", response?.Error ?? "连不上守护服务。");
            return;
        }

        Dispose();
        System.Windows.Application.Current.Shutdown(0);
    }

    private void ShowNotices()
    {
        if (_status is null || _status.PendingNotices.Count == 0)
            return;
        _noticesShown = true;
        var text = string.Join("\n", _status.PendingNotices.Select(item => item.Utc + "  " + item.Message));
        var password = Dialogs.AskPassword("上次运行之后有这些记录。输入家长密码后关闭提示：\n" + text);
        if (string.IsNullOrEmpty(password))
            return;
        _ = AcknowledgeNotices(password);
    }

    private async Task AcknowledgeNotices(string password)
    {
        var response = await _pipe.RequestAsync(new IpcRequest { Op = IpcOps.AcknowledgeNotices, Password = password });
        if (response is not { Ok: true })
        {
            _noticesShown = false;
            Dialogs.Alert("没有关闭", response?.Error ?? "密码不对，下次仍会提示。");
        }
    }

    private void Exit()
    {
        Dispose();
        System.Windows.Application.Current.Shutdown(0);
    }

    private static Icon LoadIcon()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                var associated = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
                if (associated is not null)
                    return associated;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
        }

        return SystemIcons.Shield;
    }
}
