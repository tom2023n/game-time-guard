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
        menu.Items.Add(new ToolStripMenuItem("保护仍在后台运行") { Enabled = false });
        menu.Items.Add("关闭这个图标", null, (_, _) => Exit());

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
