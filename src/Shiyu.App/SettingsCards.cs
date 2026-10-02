using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// 设置卡（§6.3 / U-14）：图标 20 在左，标签与说明同一列（标签 14/20、
/// 说明 12/20 次级色、可多行），控件列右对齐、各自垂直居中；最小高 64、
/// Padding 16,12。卡宽不足 476 时控件换到说明下方——窄窗里说明先于
/// 控件让位，截断永远不是选项。
///
/// 校验错误长在出错的卡片里：说明列下多一行 Danger 12/20 带错误字形，
/// 控件描边同步转 Danger——错误离它出错的字段一步之遥（§3.9 P1）。
/// 组卡（Expander）的父行与子行共用同一套外观与错误位，只是子行更矮、
/// 左缘缩进对齐父标签（§6.3）。
/// </summary>
internal sealed class SettingsCardView
{
    /// <summary>窄卡阈值：低于它，控件从右列换到说明下方（§6.3）。</summary>
    public const double WrapThreshold = 476;

    /// <summary>子行文字左缘：16（卡内边距）+ 20（图标）+ 16（图标与标签的间距）。</summary>
    public const double SubRowIndent = 52;

    private readonly Grid _root;
    private readonly Border? _chrome;
    private readonly TextBlock _error;
    private readonly FrameworkElement _editor;
    private readonly bool _subRow;
    private readonly bool _fullBleed;

    private SettingsCardView(
        Grid root, Border? chrome, TextBlock error, FrameworkElement editor, bool subRow, bool fullBleed = false)
    {
        _root = root;
        _chrome = chrome;
        _error = error;
        _editor = editor;
        _subRow = subRow;
        _fullBleed = fullBleed;
    }

    public FrameworkElement Root => _root;

    public double MinHeight
    {
        get => _root.MinHeight;
        set => _root.MinHeight = value;
    }

    public Thickness Margin
    {
        get => _root.Margin;
        set => _root.Margin = value;
    }

    /// <summary>错误就地亮起：卡片内一行 Danger 文字 + 错误字形 + 控件描边转红。</summary>
    public void ShowError(string message)
    {
        _error.Text = message;
        (_error.Parent as FrameworkElement)!.Visibility = Visibility.Visible;
        MarkError(true);
    }

    public void ClearError()
    {
        (_error.Parent as FrameworkElement)!.Visibility = Visibility.Collapsed;
        MarkError(false);
    }

    private void MarkError(bool on)
    {
        // 控件底边转 Danger（§6.3）：输入类编辑器描边染红；别的形状（分段、
        // 下拉）至少有卡片里的那行红字在。
        if (_editor is Control box)
        {
            box.SetResourceReference(Control.BorderBrushProperty, on ? "Brush.Danger" : "Brush.Border");
        }
    }

    public static SettingsCardView Create(SettingsItem item, FrameworkElement editor)
        => Create(item.Label, item.Hint, item.Icon, editor, item.FullBleed);

    public static SettingsCardView Create(
        string label, string? hint, string? iconGlyph, FrameworkElement editor, bool fullBleed = false)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // icon
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // words
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                      // control

        var (words, error) = Words(label, hint);

        var icon = new TextBlock
        {
            Text = GlyphOf(iconGlyph),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = iconGlyph is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed,
        };
        icon.SetResourceReference(TextElement.FontFamilyProperty, "Font.Icon");
        icon.SetResourceReference(TextElement.FontSizeProperty, "Size.IconM");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        icon.Margin = new Thickness(0, 0, 16, 0);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        Grid.SetColumn(words, 1);
        grid.Children.Add(words);

        if (fullBleed)
        {
            // 高瘦编辑器（三层排除、动作清单）：占标签下方一整行，与标签
            // 左缘对齐；右列空着。
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRowSpan(icon, 2);
            Grid.SetRowSpan(words, 2);
            editor.VerticalAlignment = VerticalAlignment.Top;
            editor.HorizontalAlignment = HorizontalAlignment.Stretch;
            editor.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(editor, 1);
            Grid.SetColumn(editor, 1);
            Grid.SetColumnSpan(editor, 2);
            grid.Children.Add(editor);
        }
        else
        {
            editor.VerticalAlignment = VerticalAlignment.Center;
            editor.HorizontalAlignment = HorizontalAlignment.Right;
            editor.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(editor, 2);
            grid.Children.Add(editor);
        }

        // 壳是一个 Grid 而不是 Border：脉冲浮层（深链定位）要往卡片上叠
        // 一个透明度动画的色块，Grid 才装得下第二个孩子。
        var chrome = new Border
        {
            Child = grid,
            Padding = new Thickness(16, 12, 16, 12),
            SnapsToDevicePixels = true,
        };
        chrome.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        chrome.SetResourceReference(Border.BorderBrushProperty, "Brush.CardStroke");
        chrome.BorderThickness = new Thickness(1);
        chrome.SetResourceReference(Border.CornerRadiusProperty, "Radius.Control");

        var root = new Grid { MinHeight = 64, Tag = chrome };
        root.Children.Add(chrome);

        var card = new SettingsCardView(root, chrome, error, editor, subRow: false, fullBleed);

        // 窄卡换行：控件挪进文字列尾部，宽卡放回右列。挂在自己的
        // SizeChanged 上——卡片实际多宽才算数，页宽说话不算。
        root.SizeChanged += (_, _) => card.Relayout();
        return card;
    }

    /// <summary>
    /// 组卡里的子行（§6.3 Expander）：最小高 48，标签在左、控件在右，
    /// 左缘 52 对齐父标签；错误行同款，只是缩进跟着子行走。
    /// </summary>
    public static SettingsCardView CreateSub(string label, FrameworkElement editor)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        text.SetResourceReference(TextElement.FontSizeProperty, "Type.Body");
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        editor.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);

        var stack = new StackPanel();
        stack.Children.Add(grid);

        var errorRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var errorGlyph = new TextBlock { Text = GlyphOf("E783") };
        errorGlyph.SetResourceReference(TextElement.FontFamilyProperty, "Font.Icon");
        errorGlyph.SetResourceReference(TextElement.FontSizeProperty, "Type.Caption");
        errorGlyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Margin = new Thickness(6, 0, 0, 0),
        };
        error.SetResourceReference(TextElement.FontSizeProperty, "Type.Caption");
        error.SetResourceReference(TextBlock.LineHeightProperty, "Line.CaptionMulti");
        error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        errorRow.Children.Add(errorGlyph);
        errorRow.Children.Add(error);
        stack.Children.Add(errorRow);

        var host = new Border
        {
            Child = stack,
            Padding = new Thickness(SubRowIndent, 4, 16, 4),
        };

        var root = new Grid { MinHeight = 48 };
        root.Children.Add(host);

        return new SettingsCardView(root, null, error, editor, subRow: true);
    }

    private void Relayout()
    {
        if (_subRow || _fullBleed)
        {
            return;
        }

        var width = _chrome?.ActualWidth ?? _root.ActualWidth;
        var wide = width >= WrapThreshold;
        var column = wide ? 2 : 1;

        if (Grid.GetColumn(_editor) == column)
        {
            return;
        }

        Grid.SetColumn(_editor, column);
        _editor.HorizontalAlignment = wide ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _editor.Margin = wide ? new Thickness(16, 0, 0, 0) : new Thickness(0, 8, 0, 0);
    }

    private static (StackPanel Words, TextBlock Error) Words(string label, string? hint)
    {
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var labelView = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
        labelView.SetResourceReference(TextElement.FontSizeProperty, "Type.Body");
        labelView.SetResourceReference(TextBlock.LineHeightProperty, "Line.Body");
        labelView.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        words.Children.Add(labelView);

        if (hint is { Length: > 0 })
        {
            var description = new TextBlock
            {
                Text = hint,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
            description.SetResourceReference(TextElement.FontSizeProperty, "Type.Caption");
            description.SetResourceReference(TextBlock.LineHeightProperty, "Line.CaptionMulti");
            description.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            words.Children.Add(description);
        }

        var errorRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var errorGlyph = new TextBlock { Text = GlyphOf("E783") };
        errorGlyph.SetResourceReference(TextElement.FontFamilyProperty, "Font.Icon");
        errorGlyph.SetResourceReference(TextElement.FontSizeProperty, "Type.Caption");
        errorGlyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Margin = new Thickness(6, 0, 0, 0),
        };
        error.SetResourceReference(TextElement.FontSizeProperty, "Type.Caption");
        error.SetResourceReference(TextBlock.LineHeightProperty, "Line.CaptionMulti");
        error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        errorRow.Children.Add(errorGlyph);
        errorRow.Children.Add(error);
        words.Children.Add(errorRow);

        return (words, error);
    }

    internal static string GlyphOf(string? code)
        => code is { Length: 4 }
            ? ((char)Convert.ToInt32(code, 16)).ToString()
            : string.Empty;
}

/// <summary>
/// 父子同容器表达从属（§6.3 Expander）：组卡一个 Border，父行是普通卡
/// 行，子行左缘 52 对齐父标签、最小高 48、行间细分隔线——缩进就是
/// 层级，不需要第二个框。
/// </summary>
internal static class SettingsGroup
{
    public static Border Card(params FrameworkElement[] rows)
    {
        var panel = new StackPanel();
        for (var index = 0; index < rows.Length; index++)
        {
            if (index > 0)
            {
                var divider = new Border { Height = 1, Margin = new Thickness(16, 0, 16, 0) };
                divider.SetResourceReference(Border.BackgroundProperty, "Brush.Divider");
                panel.Children.Add(divider);
            }

            panel.Children.Add(rows[index]);
        }

        var border = new Border
        {
            Child = panel,
            Margin = new Thickness(0, 0, 0, 4),
            SnapsToDevicePixels = true,
        };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.CardStroke");
        border.BorderThickness = new Thickness(1);
        border.SetResourceReference(Border.CornerRadiusProperty, "Radius.Control");
        return border;
    }
}
