using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Shiyu.App;

/// <summary>对话框按钮的视觉级别（§6.5）。</summary>
internal enum ContentDialogButtonStyle
{
    /// <summary>标准次级按钮：取消、合并导入。</summary>
    Standard,

    /// <summary>Accent 实底主按钮：可逆但影响大（更改数据位置）。</summary>
    Accent,

    /// <summary>Danger 实底：不可撤销（清空、按时间段删除、覆盖导入）。</summary>
    Danger,
}

/// <summary>
/// 一个对话框按钮：文案由调用方写全（"清空 37 条"，动词+数量，不写"确定"）。
/// <see cref="IsCancelFocus"/> 的按钮拿到默认焦点与 Enter——§6.5 的硬规则：
/// 误按 Enter 必须落在安全的一侧。
/// </summary>
internal sealed record ContentDialogButton(
    string Text,
    ContentDialogButtonStyle Style = ContentDialogButtonStyle.Standard,
    bool IsCancelFocus = false);

/// <summary>
/// 两窗共用的危险操作确认对话框（§6.5，替换 MessageBox 家族）：440 宽、
/// 圆角 8、内容内边距 24 的面板浮在一层遮罩上；Esc 与 Enter 都落在
/// 「取消」，危险按钮 Danger 实底 TextOnDanger 字。
///
/// 用无边框子窗口而不是 Popup：它必须像 MessageBox 一样挡住整个属主，
/// 而给每个调用窗挂遮罩 Grid 的改法侵入太大。
/// </summary>
internal static class ContentDialog
{
    /// <summary>
    /// Shows the dialog and answers the index of the clicked button, or null
    /// when the dialog was dismissed (Esc 或直接关掉)。
    /// </summary>
    public static int? Show(Window owner, string title, object body, params ContentDialogButton[] buttons)
    {
        if (buttons.Length == 0)
        {
            throw new ArgumentException("A dialog without buttons cannot be answered.", nameof(buttons));
        }

        var answer = null as int?;

        var window = new Window
        {
            Title = title,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        window.SetResourceReference(TextElement.FontFamilyProperty, "Font.Ui");
        window.SetResourceReference(TextElement.FontSizeProperty, "Type.Body");
        window.SetResourceReference(TextElement.ForegroundProperty, "Brush.Text");

        var scrim = new Border { Background = ScrimBrush() };
        var host = new Grid();

        var surface = new Border();
        surface.SetResourceReference(FrameworkElement.StyleProperty, "ContentDialogSurface");
        surface.Width = 440;
        surface.VerticalAlignment = VerticalAlignment.Center;
        surface.Margin = new Thickness(24, 0, 24, 0);

        var panel = new StackPanel();

        var heading = new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 12) };
        heading.SetResourceReference(FrameworkElement.StyleProperty, "ContentDialogTitle");
        panel.Children.Add(heading);

        if (body is string text)
        {
            var message = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
            message.SetResourceReference(FrameworkElement.StyleProperty, "ContentDialogBody");
            message.SetResourceReference(TextBlock.LineHeightProperty, "Line.CaptionMulti");
            panel.Children.Add(message);
        }
        else if (body is FrameworkElement element)
        {
            element.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(element);
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0),
        };

        Button? cancelFocus = null;
        foreach (var (button, index) in buttons.Select((button, index) => (button, index)))
        {
            var captured = index;
            var view = new Button
            {
                Content = button.Text,
                Padding = new Thickness(16, 5, 16, 5),
                MinWidth = 96,
                Margin = new Thickness(0, 0, row.Children.Count == 0 ? 0 : 8, 0),
                Cursor = Cursors.Hand,
            };

            switch (button.Style)
            {
                case ContentDialogButtonStyle.Accent:
                    view.SetResourceReference(Control.BackgroundProperty, "Brush.Accent");
                    view.SetResourceReference(Control.ForegroundProperty, "Brush.TextOnAccent");
                    view.BorderThickness = new Thickness(0);
                    break;

                case ContentDialogButtonStyle.Danger:
                    view.SetResourceReference(Control.BackgroundProperty, "Brush.Danger");
                    view.SetResourceReference(Control.ForegroundProperty, "Brush.TextOnDanger");
                    view.BorderThickness = new Thickness(0);
                    break;
            }

            if (button.IsCancelFocus)
            {
                cancelFocus = view;
            }

            view.Click += (_, _) =>
            {
                answer = captured;
                window.Close();
            };
            row.Children.Add(view);
        }

        panel.Children.Add(row);
        surface.Child = panel;
        host.Children.Add(surface);
        scrim.Child = host;
        window.Content = scrim;

        // 盖在属主身上：矩形与属主一致，面板在遮罩里居中。
        PositionOver(window, owner);

        // Enter 落在取消上（§6.5）：默认焦点即取消钮，Esc 直接按"关闭"答复。
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                window.Close();
            }
        };

        window.Loaded += (_, _) =>
        {
            (cancelFocus ?? row.Children.OfType<Button>().Last()).Focus();
        };

        window.Owner = owner;
        window.ShowDialog();
        return answer;
    }

    /// <summary>遮罩：黑三成，压暗的是"底下那个窗仍在"这件事，不是底下那扇窗的字。</summary>
    private static Brush ScrimBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0x4D, 0x00, 0x00, 0x00));
        brush.Freeze();
        return brush;
    }

    private static void PositionOver(Window window, Window owner)
    {
        // Owner 还没布局完时 ActualWidth 可能是 0——用屏幕工作区兜底居中。
        if (owner.ActualWidth > 0 && owner.ActualHeight > 0)
        {
            window.Width = owner.ActualWidth;
            window.Height = owner.ActualHeight;
            window.Left = owner.Left;
            window.Top = owner.Top;
        }
        else
        {
            window.Width = SystemParameters.WorkArea.Width;
            window.Height = SystemParameters.WorkArea.Height;
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }
}
