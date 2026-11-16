using System.Windows;
using System.Windows.Controls;

namespace GameTimeGuard.Tray;

internal static class Dialogs
{
    public static void Alert(string title, string message)
    {
        var window = Create(title, 460, 240);
        var text = Theme.Body(message);
        var close = Theme.Primary("知道了", () => window.Close());
        window.Content = Stack(text, Buttons(close));
        window.ShowDialog();
    }

    public static string? AskPassword(string prompt)
    {
        var window = Create("家长密码", 460, 250);
        var box = Theme.Secret();
        string? result = null;
        var ok = Theme.Primary("确定", () =>
        {
            result = box.Password;
            window.DialogResult = true;
        });
        var cancel = Theme.Quiet("取消", () => window.DialogResult = false);
        window.Content = Stack(Theme.Body(prompt), box, Buttons(ok, cancel));
        box.KeyDown += (_, args) =>
        {
            if (args.Key == System.Windows.Input.Key.Enter)
                ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        return window.ShowDialog() == true ? result : null;
    }

    public static void ShowNotice(string title, string message, int? countdownSeconds = null)
    {
        var window = Create(title, 520, 280);
        window.Topmost = true;
        var body = Theme.Body(message);
        body.FontSize = 18;
        var close = Theme.Primary("我知道了", () => window.Close());
        var panel = new StackPanel();
        panel.Children.Add(Theme.Title(title, 24));
        panel.Children.Add(body);
        if (countdownSeconds is > 0)
        {
            var count = Theme.Title($"{countdownSeconds} 秒", 32);
            count.Margin = new Thickness(0, 12, 0, 0);
            count.Foreground = Theme.Cinnabar;
            panel.Children.Add(count);
            var remaining = countdownSeconds.Value;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                remaining--;
                count.Text = remaining > 0 ? $"{remaining} 秒" : "正在关闭";
                if (remaining <= 0)
                    timer.Stop();
            };
            window.Closed += (_, _) => timer.Stop();
            timer.Start();
        }

        panel.Children.Add(Buttons(close));
        window.Content = new Border { Padding = new Thickness(24), Child = panel };
        window.Show();
    }

    private static Window Create(string title, double width, double height)
    {
        var window = new Window { Title = title, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Manual };
        Theme.Chrome(window, width, height);
        window.MinWidth = width;
        window.MinHeight = height;
        return window;
    }

    private static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        foreach (var child in children)
        {
            if (child is FrameworkElement element)
                element.Margin = new Thickness(0, 0, 0, 14);
            panel.Children.Add(child);
        }
        return panel;
    }

    private static StackPanel Buttons(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in buttons)
        {
            button.Margin = new Thickness(8, 0, 0, 0);
            panel.Children.Add(button);
        }
        return panel;
    }
}

internal static class UninstallGate
{
    public static bool Run()
    {
        var password = Dialogs.AskPassword("卸载游戏时间守护需要家长密码。取消后不会卸载。");
        if (string.IsNullOrEmpty(password))
            return false;

        using var pipe = new GuardPipe();
        var response = pipe.RequestAsync(new GameTimeGuard.Core.Ipc.IpcRequest
        {
            Op = GameTimeGuard.Core.Ipc.IpcOps.AuthorizeUninstall,
            Password = password
        }).GetAwaiter().GetResult();
        if (response is { Ok: true })
            return true;
        if (LocalAuth.Verify(password))
            return true;

        Dialogs.Alert("不能卸载", response?.Error ?? "密码不正确，或者现在核对不了家长密码。请先启动“GameTimeGuard”服务后再卸载。");
        return false;
    }
}
