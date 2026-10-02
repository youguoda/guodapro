using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// 悬停动作清单（§5.1「窄条」页）：从逗号分隔的字符串换成一列可拖动排序、
/// 带开关的行。顺序即显示顺序；关掉的动作沉到列表底部（顺序仍保留在
/// 全列表里，再打开不必重排）。每一次拖放与开关都即时落盘。
/// </summary>
internal sealed class ActionsListCard
{
    private const string DragGlyph = "\uE700";

    private readonly Action<string> _commit;
    private readonly StackPanel _list = new();
    private readonly List<string> _order = [];

    public FrameworkElement Element { get; }

    public ActionsListCard(AppSettings baseline, Action<string> commit)
    {
        _commit = commit;

        // 全列表 = 已启用的顺序 + 未启用的（保留 HoverActions 的默认序）。
        _order.AddRange(baseline.BarActions);
        foreach (var id in HoverActions.All.Where(id => !_order.Contains(id)))
        {
            _order.Add(id);
        }

        Element = Build(baseline);
    }

    private FrameworkElement Build(AppSettings baseline)
    {
        var enabled = baseline.BarActions.ToHashSet(StringComparer.Ordinal);
        foreach (var id in _order.ToList())
        {
            _list.Children.Add(Row(id, enabled.Contains(id)));
        }

        var host = new StackPanel();
        host.Children.Add(_list);

        var note = new TextBlock
        {
            Text = "拖动左侧按钮调整顺序；关掉的动作沉底，再打开不必重排。悬停某条记录时，不适用的动作会自动隐藏。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        note.SetResourceReference(TextElement.FontSizeProperty, "Type.Caption");
        note.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        host.Children.Add(note);

        return host;
    }

    private Border Row(string id, bool on)
    {
        var label = new TextBlock
        {
            Text = HoverActions.Name(id),
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(TextElement.FontSizeProperty, "Type.Body");
        label.SetResourceReference(TextBlock.ForegroundProperty, on ? "Brush.Text" : "Brush.TextTertiary");

        var toggle = new CheckBox
        {
            IsChecked = on,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggle.SetResourceReference(FrameworkElement.StyleProperty, "ToggleSwitch");
        toggle.Checked += (_, _) =>
        {
            label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            Save();
        };
        toggle.Unchecked += (_, _) =>
        {
            label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextTertiary");
            Save();
        };

        var handle = new TextBlock
        {
            Text = DragGlyph,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "拖动调整顺序",
        };
        handle.SetResourceReference(TextElement.FontFamilyProperty, "Font.Icon");
        handle.SetResourceReference(TextElement.FontSizeProperty, "Type.Caption");
        handle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        handle.Margin = new Thickness(0, 0, 10, 0);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(handle, 0);
        grid.Children.Add(handle);
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        Grid.SetColumn(toggle, 2);
        grid.Children.Add(toggle);

        var row = new Border
        {
            Child = grid,
            Padding = new Thickness(4, 3, 4, 3),
            Tag = id,
        };

        // 拖动排序：句柄按下后按行中线换位，松手提交。
        handle.MouseLeftButtonDown += (_, e) => BeginDrag(row, e);
        return row;
    }

    private void BeginDrag(Border row, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var captured = (string)row.Tag;
        Mouse.Capture(row);

        void Move(object sender, MouseEventArgs args)
        {
            var target = RowAt(args.GetPosition(_list).Y);
            if (target is null || ReferenceEquals(target, row))
            {
                return;
            }

            var to = _list.Children.IndexOf(target);
            _list.Children.Remove(row);
            _list.Children.Insert(to, row);

            _order.Remove(captured);
            var idTo = _order.IndexOf((string)target.Tag);
            _order.Insert(idTo >= 0 ? idTo : _order.Count, captured);
        }

        void Up(object sender, MouseButtonEventArgs args)
        {
            row.MouseMove -= Move;
            row.MouseLeftButtonUp -= Up;
            if (Mouse.Captured == row)
            {
                Mouse.Capture(null);
            }

            Save();
        }

        row.MouseMove += Move;
        row.MouseLeftButtonUp += Up;
    }

    private Border? RowAt(double y)
        => _list.Children.OfType<Border>()
            .FirstOrDefault(child =>
            {
                var position = child.TransformToAncestor(_list).Transform(new Point(0, 0));
                return y >= position.Y && y <= position.Y + child.ActualHeight;
            });

    /// <summary>顺序 + 开关折回逗号分隔的启用清单（写入 BarActions）。</summary>
    private void Save()
    {
        var enabled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var border in _list.Children.OfType<Border>())
        {
            if (border.Child is Grid grid
                && grid.Children.OfType<CheckBox>().FirstOrDefault() is { IsChecked: true }
                && border.Tag is string id)
            {
                enabled.Add(id);
            }
        }

        var ordered = _order.Where(enabled.Contains).ToList();
        _commit(string.Join(",", ordered.Select(HoverActions.Name)));
    }
}
