using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameTimeGuard.Core.Ipc;
using GameTimeGuard.Core.Schedule;

namespace GameTimeGuard.Tray;

internal sealed class WizardWindow : Window
{
    private readonly GuardPipe _pipe;
    private readonly SettingsUpdate _settings = DefaultPolicy.Create();
    private readonly StackPanel _body = new();
    private readonly TextBlock _stepLabel;
    private int _step;
    private readonly PasswordBox _password = Theme.Secret();
    private readonly PasswordBox _confirm = Theme.Secret();
    private readonly TextBox _sunday = Theme.Field();
    private readonly TextBox _weekday = Theme.Field();
    private readonly CheckBox _steam = new() { Content = "Steam 游戏", IsChecked = true, Margin = new Thickness(0, 4, 12, 4) };
    private readonly CheckBox _wegame = new() { Content = "WeGame", IsChecked = true, Margin = new Thickness(0, 4, 12, 4) };
    private readonly CheckBox _delta = new() { Content = "三角洲行动", IsChecked = true, Margin = new Thickness(0, 4, 12, 4) };

    public WizardWindow(GuardPipe pipe)
    {
        _pipe = pipe;
        Title = "游戏时间守护 · 开始设置";
        Theme.Chrome(this, 680, 560);
        _sunday.Text = "180";
        _weekday.Text = "120";
        _stepLabel = Theme.Body("", muted: true);
        var back = Theme.Quiet("上一步", () => ShowStep(_step - 1));
        var next = Theme.Primary("下一步", Next);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(back);
        buttons.Children.Add(next);
        var root = new DockPanel { Margin = new Thickness(28) };
        DockPanel.SetDock(_stepLabel, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(_stepLabel);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body });
        Content = root;
        ShowStep(0);
    }

    private void Next()
    {
        if (_step == 1 && !PasswordsMatch())
            return;
        if (_step == 2)
        {
            _ = FinishAsync();
            return;
        }

        ShowStep(_step + 1);
    }

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, 3);
        _stepLabel.Text = $"第 {_step + 1} 步，共 4 步";
        _body.Children.Clear();
        switch (_step)
        {
            case 0:
                Add(Theme.Title("先把规矩定下来"));
                Add(Theme.Body("游戏时间守护会在后台记住今天已经玩了多久。快用完时它会提醒；到点或超出允许时段后，先等 30 秒，再结束正在玩的游戏。"));
                Add(Theme.Body("孩子的账户是不是管理员都可以用。没有家长密码就不能从这里关掉保护。懂行的管理员仍可能绕过，绕过会被记下来。", muted: true));
                if (AdminAccount.IsAdministrator())
                    Add(Banner(AdminAccount.Warning));
                break;
            case 1:
                Add(Theme.Title("设置家长密码"));
                Add(Theme.Body("改时长、临时加时和卸载都要这个密码。它只以加盐散列保存，程序里没有明文。建议 8 位以上，不要用孩子知道的生日。"));
                Add(Label("密码"));
                Add(_password);
                Add(Label("再输入一次"));
                Add(_confirm);
                break;
            case 2:
                Add(Theme.Title("确认默认规则"));
                Add(Theme.Body("这套默认和以前每分钟运行的脚本一致：周日久一些，其他日子 120 分钟，全天可以玩。允许时段可以装好以后再改，例如每天 18:00–21:00。"));
                Add(Label("周日分钟"));
                Add(_sunday);
                Add(Label("周一到周六分钟"));
                Add(_weekday);
                var row = new WrapPanel();
                row.Children.Add(_steam);
                row.Children.Add(_wegame);
                row.Children.Add(_delta);
                Add(row);
                Add(Theme.Body("只改游戏 exe 的名字绕不过目录规则。把整个游戏搬到无关的文件夹时，需要在设置里把那个文件夹加进来。", muted: true));
                break;
            default:
                Add(Theme.Title("已经开始保护"));
                Add(Theme.Body("图标在屏幕右下角。关闭图标不会停止保护，服务会再把它打开。"));
                Add(Theme.Body("下一步请给孩子建一个本地标准账户：设置 → 账户 → 其他用户 → 添加账户，选择没有微软账号，账户类型选标准用户。"));
                Add(Theme.Primary("完成", Close));
                break;
        }
    }

    private bool PasswordsMatch()
    {
        if (_password.Password != _confirm.Password)
        {
            Dialogs.Alert("密码不一致", "两次输入的家长密码不一样。");
            return false;
        }

        if (_password.Password.Length < 4)
        {
            Dialogs.Alert("密码太短", "家长密码至少 4 位，建议 8 位以上。");
            return false;
        }

        return true;
    }

    private async Task FinishAsync()
    {
        if (!int.TryParse(_sunday.Text.Trim(), out var sunday) || sunday is < 0 or > 1440 ||
            !int.TryParse(_weekday.Text.Trim(), out var weekday) || weekday is < 0 or > 1440)
        {
            Dialogs.Alert("时长不对", "分钟数要在 0 到 1440 之间。");
            return;
        }

        foreach (var entry in _settings.DailyMinutes)
            entry.Minutes = entry.Day.Equals("Sun", StringComparison.OrdinalIgnoreCase) ? sunday : weekday;
        _settings.EnabledRuleIds = [];
        if (_steam.IsChecked == true)
            _settings.EnabledRuleIds.Add("steam");
        if (_wegame.IsChecked == true)
            _settings.EnabledRuleIds.Add("wegame");
        if (_delta.IsChecked == true)
            _settings.EnabledRuleIds.Add("delta-force");

        var response = await _pipe.RequestAsync(new IpcRequest
        {
            Op = IpcOps.Setup,
            Password = _password.Password,
            Settings = _settings
        });
        if (response is not { Ok: true })
        {
            Dialogs.Alert("还没设置好", response?.Error ?? "连不上守护服务。请确认 GameTimeGuard 服务已启动，然后重试。");
            return;
        }

        ShowStep(3);
    }

    private void Add(UIElement element)
    {
        if (element is FrameworkElement framework)
            framework.Margin = new Thickness(0, 0, 0, 12);
        _body.Children.Add(element);
    }

    private static TextBlock Label(string text)
    {
        var block = Theme.Body(text, muted: true);
        block.Margin = new Thickness(0, 0, 0, 4);
        return block;
    }

    private static Border Banner(string text) => Theme.CardPanel(Theme.Body(text), Color.FromRgb(246, 226, 184));
}
