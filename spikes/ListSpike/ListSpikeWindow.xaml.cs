using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ListSpike;

internal partial class ListSpikeWindow : Window
{
    private CardData? _selected;

    public ListSpikeWindow(int count)
    {
        InitializeComponent();

        var deck = Deck.Generate(count);
        var pinnedIndexes = new HashSet<int> { 5, 40, 61 };

        foreach (var card in deck.Where(c => pinnedIndexes.Contains(c.Index)))
        {
            card.IsPinned = true;
        }

        Pinned = new ObservableCollection<CardData>(deck.Where(c => c.IsPinned));
        Rest = new ObservableCollection<CardData>(deck.Where(c => !c.IsPinned));

        PinnedList.ItemsSource = Pinned;
        MainList.ItemsSource = Rest;

        Title = $"Shiyu list spike — {count} 条";

        // Button clicks bubble up from the tray; Tag carries the action name.
        AddHandler(Button.ClickEvent, new RoutedEventHandler(OnTrayAction));

        Loaded += (_, _) => WireStatus();
    }

    public ObservableCollection<CardData> Pinned { get; }

    public ObservableCollection<CardData> Rest { get; }

    internal Border PinnedHostControl => PinnedHost;

    internal ScrollViewer? Scroller => Tree.FindDescendant<ScrollViewer>(MainList);

    internal IReadOnlyList<CardContainer> RealizedContainers()
        => Tree.Descendants<CardContainer>(MainList).ToList();

    internal IReadOnlyList<double> RealizedHeights()
        => RealizedContainers().Select(c => c.ActualHeight).ToList();

    internal CardContainer? ContainerAt(int index)
        => MainList.ItemContainerGenerator.ContainerFromIndex(index) as CardContainer;

    private void WireStatus()
    {
        if (Scroller is { } scroller)
        {
            // Interactive only: during the self-test this would run on every
            // scroll offset change and pollute the timing being measured.
            scroller.ScrollChanged += (_, _) =>
            {
                if (Interactive)
                {
                    UpdateStatus();
                }
            };
        }

        UpdateStatus();
    }

    /// <summary>False while the self-test drives the window.</summary>
    internal bool Interactive { get; set; } = true;

    private void UpdateStatus()
        => Status.Text = $"条目 {Pinned.Count + Rest.Count}（置顶 {Pinned.Count}） · " +
           $"容器累计创建 {CardContainer.Created} · 当前在树 {RealizedContainers().Count}";

    private void OnCardMouseEnter(object sender, MouseEventArgs e)
        => TrayOf(sender)?.Open();

    private void OnCardMouseLeave(object sender, MouseEventArgs e)
        => TrayOf(sender)?.Close();

    private static TrayStrip? TrayOf(object sender)
        => Tree.FindDescendant<TrayStrip>((DependencyObject)sender);

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is CardData card)
        {
            Select(card);
        }
    }

    private void Select(CardData card)
    {
        if (_selected == card)
        {
            return;
        }

        if (_selected is not null)
        {
            _selected.IsSelected = false;
        }

        _selected = card;
        card.IsSelected = true;
    }

    private void OnTrayAction(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { Tag: string action })
        {
            return;
        }

        if (((FrameworkElement)e.OriginalSource).DataContext is not CardData card)
        {
            return;
        }

        if (action == "钉")
        {
            TogglePin(card);
            e.Handled = true;
            return;
        }

        Status.Text = $"点了「{action}」 · 演示按钮（{card.Title}）";
    }

    private void TogglePin(CardData card)
    {
        if (card.IsPinned)
        {
            card.IsPinned = false;
            Pinned.Remove(card);

            // Keep the demo list ordered by index; a binary search is not
            // worth it at spike scale.
            var at = Rest.TakeWhile(c => c.Index < card.Index).Count();
            Rest.Insert(at, card);
        }
        else
        {
            card.IsPinned = true;
            Rest.Remove(card);
            Pinned.Add(card);
        }

        UpdateStatus();
    }
}
