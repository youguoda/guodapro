using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>
/// One card's view state. A class rather than a record because selection is
/// mutable and the card's own highlight follows it.
/// </summary>
internal sealed class BarCard : INotifyPropertyChanged
{
    public long Id { get; init; }

    public EntryKind Kind { get; init; }

    /// <summary>The full text, kept for copying; the card shows the clamped preview.</summary>
    public required string Text { get; init; }

    public string Preview { get; init; } = string.Empty;

    public string Meta { get; init; } = string.Empty;

    public ImageSource? Icon { get; init; }

    public ImageSource? Thumbnail { get; init; }

    public int TextLines { get; init; }

    /// <summary>
    /// The text clamp as a height of whole Body lines — visually identical to
    /// MaxLines, usable from XAML on this build (see the template comment).
    /// </summary>
    public double TextMaxHeight
        => TextLines * DesignTokens.LineHeightFor(DesignTokens.FontBody);

    public int ImageHeight { get; init; }

    public bool IsPinned { get; init; }

    public Visibility IconVisibility => Icon is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility FallbackIconVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ImageVisibility =>
        Kind == EntryKind.Image ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PinnedVisibility => IsPinned ? Visibility.Visible : Visibility.Collapsed;

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            Changed(nameof(IsSelected));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// The resident narrow bar (ticket 12): about 360px wide, no system chrome,
/// topmost, dragged by its background and resized by a grip, summoned and
/// hidden by hotkey. It serves the dozens-of-times-a-day quick reads; the
/// full library window stays for the weekly tidy and is untouched by this.
///
/// List structure follows the issue 02 spike: pinned cards stacked above the
/// scrolling region (visually identical to overlaying, no scroll sync), main
/// list virtualized with recycling so ten thousand entries live on a handful
/// of containers.
/// </summary>
internal partial class BarWindow : Window
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan BehindCheck = TimeSpan.FromSeconds(2);
    private const string AnyTag = "全部";

    private readonly EntryStore _store;
    private readonly AppIconCache _icons;
    private readonly WindowsClipboardWriter _clipboard;
    private readonly HistoryBrowser _browser;
    private readonly ObservableCollection<BarCard> _pinned = [];
    private readonly ObservableCollection<BarCard> _cards = [];
    private readonly System.Windows.Threading.DispatcherTimer _searchDebounce;
    private readonly System.Windows.Threading.DispatcherTimer _behindCheck;
    private BarCard? _selected;
    private AppSettings _settings;
    private int _seenCount = -1;
    private bool _refillingTags;

    /// <summary>Raised when the window is moved or resized; the owner persists geometry, throttled its own way.</summary>
    public event Action? GeometryChanged;

    public BarWindow(EntryStore store, AppIconCache icons, WindowsClipboardWriter clipboard, AppSettings settings)
    {
        InitializeComponent();

        _store = store;
        _icons = icons;
        _clipboard = clipboard;
        _settings = settings;
        _browser = new HistoryBrowser(store);

        _searchDebounce = new System.Windows.Threading.DispatcherTimer { Interval = SearchDelay };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            ApplyFilter();
        };

        // A resident window goes stale: copies keep arriving while it sits
        // open. A cheap count probe notices and reloads — only while visible.
        _behindCheck = new System.Windows.Threading.DispatcherTimer { Interval = BehindCheck };
        _behindCheck.Tick += (_, _) => ReloadIfBehind();

        PinnedList.ItemsSource = _pinned;
        Cards.ItemsSource = _cards;

        RestoreGeometry();
        RefreshTagChoices();
        Rebuild();
    }

    /// <summary>New settings apply on the spot: density clamps change, hotkey label follows.</summary>
    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        Rebuild();
    }

    // --- showing and hiding ----------------------------------------------------

    /// <summary>
    /// Summons the bar at full opacity — per the motion rule, fade-ins from
    /// transparency never composite on WPF windows.
    /// </summary>
    public void Summon()
    {
        ReloadIfBehind(force: true);
        _behindCheck.Start();
        Show();
        Activate();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>Hides with the standard fade, from a painted surface.</summary>
    public void Dismiss()
    {
        _behindCheck.Stop();

        var fade = Motion.Fade(0);
        fade.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            Dismiss();
        }
        else
        {
            Summon();
        }
    }

    // --- geometry ----------------------------------------------------------------

    public double BarLeft => Left;

    public double BarTop => Top;

    public double BarHeight => Height;

    private void RestoreGeometry()
    {
        if (_settings.BarLeft is { } left)
        {
            Left = left;
        }

        if (_settings.BarTop is { } top)
        {
            Top = top;
        }

        var work = SystemParameters.WorkArea;
        Height = Math.Clamp(_settings.BarHeight ?? 620, 320, work.Height);

        if (Left + Width > work.Right || Top + Height > work.Bottom || Left < work.Left - Width)
        {
            // A monitor layout change between sessions can strand the bar off
            // every screen; a stranded bar looks exactly like a broken hotkey.
            Left = work.Right - Width - 16;
            Top = work.Top + 16;
        }
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        GeometryChanged?.Invoke();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        GeometryChanged?.Invoke();
    }

    private void OnGripDrag(object sender, DragDeltaEventArgs e)
    {
        var work = SystemParameters.WorkArea;
        Height = Math.Clamp(Height + e.VerticalChange, 320, work.Height);
    }

    // --- background dragging -------------------------------------------------

    private void OnBackgroundPressed(object sender, MouseButtonEventArgs e)
    {
        // Card presses mark themselves handled, and input controls swallow
        // their own clicks, so what reaches here is genuinely background.
        DragMove();
    }

    // --- cards ----------------------------------------------------------------

    private BarCard CardFor(Entry entry)
    {
        var collapsed = string.Join(' ', entry.Text.Split(
            ['\r', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return new BarCard
        {
            Id = entry.Id,
            Kind = entry.Kind,
            Text = entry.Text,
            Preview = collapsed.Length > 500 ? collapsed[..500] + "…" : collapsed,
            Meta = $"{(entry.Kind == EntryKind.Image ? "图片" : "文本")} · {entry.CreatedAt.ToLocalTime():MM-dd HH:mm}",
            Icon = _icons.For(entry.SourceApp),
            Thumbnail = entry.Kind == EntryKind.Image
                ? AppIconCache.Decode(entry.ThumbnailPng, 320)
                : null,
            TextLines = Math.Max(1, _settings.BarTextLines),
            ImageHeight = Math.Max(24, _settings.BarImageHeight),
            IsPinned = entry.IsPinned,
        };
    }

    private void Rebuild()
    {
        _pinned.Clear();
        _cards.Clear();
        _selected = null;
        Append(_browser.Loaded);
        PinnedHost.Visibility = _pinned.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCount();
    }

    private void Append(IEnumerable<Entry> entries)
    {
        // The store orders pinned first, so the partition is one pass: once
        // the first unpinned entry arrives, everything after it is too.
        var pastPinned = _cards.Count > 0;

        foreach (var entry in entries)
        {
            if (!entry.IsPinned)
            {
                pastPinned = true;
            }

            (pastPinned ? _cards : _pinned).Add(CardFor(entry));
        }
    }

    private void UpdateCount()
        => CountLabel.Text = $"共 {_store.Count()} 条 · Esc 隐藏";

    private void ReloadIfBehind(bool force = false)
    {
        var count = _store.Count();

        if (!force && (count == _seenCount || !IsVisible))
        {
            return;
        }

        _seenCount = count;
        _browser.Reset();
        RefreshTagChoices();
        Rebuild();
    }

    private void OnCardsScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (!_browser.HasMore
            || e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 400)
        {
            return;
        }

        var added = _browser.LoadMore();
        if (added > 0)
        {
            Append(_browser.Loaded.Skip(_browser.Loaded.Count - added));
        }
    }

    // --- filters ----------------------------------------------------------------

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && !_refillingTags)
        {
            ApplyFilter();
        }
    }

    private void OnClearFilters(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        KindFilter.SelectedIndex = 0;
        RefreshTagChoices();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _browser.Filter = new HistoryFilter
        {
            Query = SearchBox.Text,
            Tag = TagFilter.SelectedItem as string is { } tag && tag != AnyTag ? tag : null,
            Kind = KindFilter.SelectedIndex switch
            {
                1 => EntryKind.Text,
                2 => EntryKind.Image,
                _ => null,
            },
        };

        Rebuild();
    }

    private void RefreshTagChoices()
    {
        var chosen = TagFilter.SelectedItem as string;

        _refillingTags = true;
        TagFilter.Items.Clear();
        TagFilter.Items.Add(AnyTag);
        foreach (var tag in _store.AllTags())
        {
            TagFilter.Items.Add(tag);
        }

        TagFilter.SelectedItem = chosen is { } name && TagFilter.Items.Contains(name) ? chosen : AnyTag;
        _refillingTags = false;
    }

    // --- interaction ---------------------------------------------------------

    private void OnCardPressed(object sender, MouseButtonEventArgs e)
    {
        // Handled, so the press does not turn into a window drag.
        e.Handled = true;

        if (((FrameworkElement)sender).DataContext is not BarCard card)
        {
            return;
        }

        Select(card);

        // Border is not a Control and has no double-click of its own; the
        // count on the press is the same information.
        if (e.ClickCount == 2)
        {
            _clipboard.SetText(card.Text);
        }
    }

    private void Select(BarCard? card)
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

        if (card is not null)
        {
            card.IsSelected = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Dismiss();
            e.Handled = true;
        }
    }
}
