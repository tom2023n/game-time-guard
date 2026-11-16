using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GameTimeGuard.Tray;

internal static class Theme
{
    public static readonly FontFamily Font = new("Microsoft YaHei UI, Segoe UI");
    public static readonly SolidColorBrush Paper = Freeze(Color.FromRgb(243, 238, 228));
    public static readonly SolidColorBrush Ink = Freeze(Color.FromRgb(27, 36, 48));
    public static readonly SolidColorBrush Muted = Freeze(Color.FromRgb(90, 99, 112));
    public static readonly SolidColorBrush Teal = Freeze(Color.FromRgb(14, 107, 92));
    public static readonly SolidColorBrush TealDark = Freeze(Color.FromRgb(11, 61, 54));
    public static readonly SolidColorBrush Card = Freeze(Color.FromRgb(255, 252, 247));
    public static readonly SolidColorBrush Line = Freeze(Color.FromRgb(228, 217, 200));
    public static readonly SolidColorBrush Amber = Freeze(Color.FromRgb(246, 226, 184));
    public static readonly SolidColorBrush Cinnabar = Freeze(Color.FromRgb(178, 58, 46));
    public static readonly SolidColorBrush Moss = Freeze(Color.FromRgb(231, 242, 238));

    public static void Chrome(Window window, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        window.MinWidth = Math.Min(720, width);
        window.MinHeight = Math.Min(520, height);
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Background = Paper;
        window.FontFamily = Font;
        window.FontSize = 15;
        window.Foreground = Ink;
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
    }

    public static TextBlock Title(string text, double size = 26) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        Foreground = Ink,
        TextWrapping = TextWrapping.Wrap
    };

    public static TextBlock Body(string text, bool muted = false) => new()
    {
        Text = text,
        Foreground = muted ? Muted : Ink,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 24
    };

    public static Button Primary(string text, Action click) => MakeButton(text, Teal, Brushes.White, click);

    public static Button Quiet(string text, Action click) => MakeButton(text, Card, Ink, click, Line);

    public static TextBox Field() => new()
    {
        Padding = new Thickness(10, 8, 10, 8),
        FontSize = 15,
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        Background = Card
    };

    public static PasswordBox Secret() => new()
    {
        Padding = new Thickness(10, 8, 10, 8),
        FontSize = 15,
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        Background = Card
    };

    public static Border CardPanel(UIElement child, Color? background = null) => new()
    {
        Background = background is null ? Card : Freeze(background.Value),
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(16),
        Child = child
    };

    public static string Clock(int seconds)
    {
        if (seconds < 0)
            seconds = 0;
        var hours = seconds / 3600;
        var minutes = (seconds % 3600) / 60;
        var remain = seconds % 60;
        return hours > 0 ? $"{hours} 小时 {minutes:00} 分" : $"{minutes} 分 {remain:00} 秒";
    }

    public static string PhaseText(string? phase) => phase switch
    {
        "setup" => "还没有完成家长初始化",
        "grace" => "即将关闭游戏",
        "terminate" => "正在关闭游戏",
        "exhausted" => "今天的时间已经用完",
        _ => "今天还可以玩"
    };

    private static Button MakeButton(string text, Brush background, Brush foreground, Action click, Brush? border = null)
    {
        var button = new Button
        {
            Content = text,
            Foreground = foreground,
            Background = background,
            BorderBrush = border ?? background,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 8, 18, 8),
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 15,
            Cursor = Cursors.Hand,
            Template = FlatTemplate()
        };
        button.Click += (_, _) => click();
        return button;
    }

    private static ControlTemplate FlatTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
