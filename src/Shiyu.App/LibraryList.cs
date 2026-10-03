using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.ComponentModel;
using System.Windows.Documents;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// 管理窗列表的行容器附加属性（票 24）：多选模式下每行左侧长出复选框——
/// 静止时不存在，不占位。行模板用祖先绑定读它（DataTemplate 触发器），容器
/// 准备时由列表按 MultiCheckMode 写入，选择数变化时窗口对已实现的行逐一更新。
/// </summary>
internal static class RowProps
{
    public static readonly DependencyProperty ShowCheckProperty = DependencyProperty.RegisterAttached(
        "ShowCheck", typeof(bool), typeof(RowProps), new FrameworkPropertyMetadata(false));

    public static bool GetShowCheck(DependencyObject element)
        => (bool)element.GetValue(ShowCheckProperty);

    public static void SetShowCheck(DependencyObject element, bool value)
        => element.SetValue(ShowCheckProperty, value);
}

/// <summary>
/// 管理窗列表（§6.4）：一个虚拟化 ListBox，按 <see cref="EntryItem.GroupKey"/>
/// 走 CollectionView 分组——分组头是 GroupItem，天生不可选不可聚焦，↑↓ 与
/// Ctrl+A 自然越过；行高（56/72）在容器准备时按数据写入，回收复用跟着走。
/// 吸顶条浮在列表上缘，窗口在滚动回调里维护它。
/// </summary>
internal sealed class LibraryList : ListBox
{
    public LibraryList()
    {
        // 分组后默认整体实例化（老 WPF 行为），必须显式打开分组虚拟化。
        VirtualizingPanel.SetIsVirtualizingWhenGrouping(this, true);
    }

    /// <summary>滚动位置变化（吸顶与滚动预取都挂在上面）；窗口在构造后订阅。</summary>
    public event EventHandler<ScrollChangedEventArgs>? ListScrolled;

    /// <summary>
    /// 多选复选框模式：选择数 ≥ 2 时为真。此后准备的容器按它决定复选框的
    /// 出现（RowProps.ShowCheck）；已实现的行由窗口即刻更新。
    /// </summary>
    public bool MultiCheckMode { get; set; }

    /// <summary>
    /// The list's own scroll viewer, found once the template is applied. Both
    /// the sticky header and the page-ahead prefetch read positions from it.
    /// </summary>
    public ScrollViewer? Scroller { get; private set; }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        Scroller = Template.FindName("PART_ScrollViewer", this) as ScrollViewer;
        if (Scroller is not null)
        {
            Scroller.ScrollChanged += (_, e) => ListScrolled?.Invoke(this, e);
        }
    }

    /// <summary>
    /// 当前视口里最上面那条记录所属的分组（吸顶条显示它）；null = 列表还在
    /// 顶部，不需要吸顶。用"第一条越过吸顶线下缘的行"判定，配合分组头与
    /// 吸顶条同高，切换点正好落在分组头完全滚出之后。
    /// </summary>
    public string? GroupAtViewportTop(double stickyHeight)
    {
        if (Scroller is null)
        {
            return null;
        }

        string? current = null;
        foreach (var item in Items)
        {
            if (ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem row
                || !row.IsArrangeValid)
            {
                continue;
            }

            var y = row.TranslatePoint(new Point(), Scroller).Y;
            if (y >= stickyHeight - 4)
            {
                current = (item as EntryItem)?.GroupKey;
                break;
            }

            // 还在吸顶线上方的行：它属于"当前"组——记住最新的那个，遇到第一条
            // 越线行之前一直更新。
            if (item is EntryItem entry)
            {
                current = entry.GroupKey;
            }
        }

        return current;
    }

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);

        // 文本行 56 / 图片行 72（§6.4）：行高是行自己的事——回收容器复用时
        // 高度跟着新数据走，模板不用分支。
        if (element is ListBoxItem row)
        {
            row.Height = item is EntryItem { Kind: EntryKind.Image } image && image.Thumbnail is not null
                ? DesignTokens.ListRowHeightImage
                : DesignTokens.ListRowHeight;
            RowProps.SetShowCheck(row, MultiCheckMode);
        }
    }
}

/// <summary>
/// 分组的排序（票 24）：置顶最前，其余新到旧。交给视图的 CustomSort，
/// 分组的出现顺序由此决定，不依赖"先见到谁"的偶然。
/// </summary>
internal sealed class EntryRowOrder : System.Collections.IComparer
{
    public static readonly EntryRowOrder Instance = new();

    public int Compare(object? x, object? y)
    {
        if (x is not EntryItem a || y is not EntryItem b)
        {
            return 0;
        }

        var byGroup = HistoryGroups.Rank(a.GroupKey).CompareTo(HistoryGroups.Rank(b.GroupKey));
        if (byGroup != 0)
        {
            return byGroup;
        }

        var byTime = b.CreatedAt.CompareTo(a.CreatedAt);
        return byTime != 0 ? byTime : a.Id.CompareTo(b.Id);
    }
}

/// <summary>
/// 分组头的视觉（§6.4 列表）：Caption 号次级色 + 分组内条数。GroupItem 的
/// DataContext 是 CollectionViewGroup，Name 即分组键。
/// </summary>
internal sealed class GroupHeaderView : Border
{
    public GroupHeaderView()
    {
        SetResourceReference(HeightProperty, "Control.HeightCompact");
        BorderThickness = new Thickness(0, 0, 0, 1);
        SetResourceReference(BorderBrushProperty, "Brush.Divider");

        var label = new TextBlock
        {
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(TextBlock.FontSizeProperty, "Type.Caption");
        label.SetResourceReference(TextElement.FontWeightProperty, "Weight.Emphasis");
        label.SetBinding(TextBlock.TextProperty, new Binding("Name"));

        var count = new TextBlock
        {
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        count.SetResourceReference(TextBlock.FontSizeProperty, "Type.Caption");
        count.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        count.SetBinding(TextBlock.TextProperty, new Binding("ItemCount") { StringFormat = "{0} 条" });

        var panel = new DockPanel { Margin = new Thickness(6, 0, 6, 0), LastChildFill = false };
        DockPanel.SetDock(count, Dock.Right);
        panel.Children.Add(label);
        panel.Children.Add(count);
        Child = panel;
    }
}
