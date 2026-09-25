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
/// The timestamp that yields its place to the hover actions. Fades and slides
/// left when the tray opens, returns when it closes — the two read as one
/// handover rather than as two separate events.
/// </summary>
internal sealed class HandoverText : TextBlock
{
    public HandoverText()
    {
        RenderTransform = new TranslateTransform();
    }

    public void Yield()
    {
        BeginAnimation(OpacityProperty, Motion.Fade(0));

        if (RenderTransform is TranslateTransform slide)
        {
            slide.BeginAnimation(TranslateTransform.XProperty, Motion.Double(-8));
        }
    }

    public void Return()
    {
        BeginAnimation(OpacityProperty, Motion.Fade(1));

        if (RenderTransform is TranslateTransform slide)
        {
            slide.BeginAnimation(TranslateTransform.XProperty, Motion.Double(0));
        }
    }

    /// <summary>Snaps back to the resting state, recycling-safe.</summary>
    public void Reset()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        if (RenderTransform is TranslateTransform slide)
        {
            slide.BeginAnimation(TranslateTransform.XProperty, null);
            slide.X = 0;
        }
    }
}

/// <summary>
/// The key badge that covers a row's source-app icon while Ctrl is held,
/// showing the number key that pastes that row. Opaque, same sixteen units,
/// exactly over the icon: one element to toggle, no layout movement.
/// </summary>
internal sealed class RowKeyBadge : Border
{
    public RowKeyBadge()
    {
        CornerRadius = new CornerRadius(3);
        SetResourceReference(BackgroundProperty, "Brush.Accent");

        var label = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(TextBlock.FontSizeProperty, "Size.Hint");
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextOnAccent");
        label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("RowKeyText"));
        Child = label;
    }
}

/// <summary>
/// The row container. Exists to reconfigure and reset the hover tray at
/// exactly the moment recycling hands this container to another row — the
/// issue 02 spike's finding about animation state following containers.
/// </summary>
internal sealed class BarCardContainer : ListBoxItem
{
    public BarCardContainer()
    {
        // Recycling in a nutshell: the same container instance gets a new
        // row's DataContext.
        DataContextChanged += (_, _) => PrepareTray();
    }

    public void PrepareTray()
    {
        var tray = Tree.FindDescendant<ActionTray>(this);
        var handover = Tree.FindDescendant<HandoverText>(this);
        var badge = Tree.FindDescendant<RowKeyBadge>(this);

        if (DataContext is not BarCard card || tray is null)
        {
            return;
        }

        if (Window.GetWindow(this) is BarWindow host)
        {
            tray.Configure(card, host.ActionsFor(card));

            // Resubscribed rather than accumulated: the tray instance
            // survives recycling and would otherwise fire twice per press.
            tray.ActionExecuted -= host.RunHoverAction;
            tray.ActionExecuted += host.RunHoverAction;

            // A row realized while Ctrl is held must arrive with its badge.
            host.ApplyKeyHintsTo(badge, tray);
        }

        tray.Reset();
        handover?.Reset();
    }
}

internal sealed class BarCardsList : ListBox
{
    protected override DependencyObject GetContainerForItemOverride()
        => new BarCardContainer();

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);

        if (element is BarCardContainer card)
        {
            card.PrepareTray();
        }
    }

    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is BarCardContainer card)
        {
            card.PrepareTray();
        }

        base.ClearContainerForItemOverride(element, item);
    }
}

internal static class Tree
{
    internal static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}

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

    public string KindText { get; init; } = string.Empty;

    public string WhenText { get; init; } = string.Empty;

    /// <summary>
    /// The number key that pastes this row, when it is one of the first ten
    /// displayed rows; null otherwise. Assigned from display position.
    /// </summary>
    public string? RowKeyText { get; set; }

    public ImageSource? Icon { get; init; }

    public ImageSource? Thumbnail { get; init; }

    public string? OriginalPath { get; init; }

    public bool HasOriginal { get; init; }

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
    private readonly SelectionCapture _capture;
    private readonly HistoryBrowser _browser;
    private readonly ObservableCollection<BarCard> _pinned = [];
    private readonly ObservableCollection<BarCard> _cards = [];
    private readonly System.Windows.Threading.DispatcherTimer _searchDebounce;
    private readonly System.Windows.Threading.DispatcherTimer _behindCheck;
    private BarCard? _selected;
    private AppSettings _settings;
    private int _seenCount = -1;
    private bool _refillingTags;
    private ForegroundWindow _returnTo;

    /// <summary>Raised when the window is moved or resized; the owner persists geometry, throttled its own way.</summary>
    public event Action? GeometryChanged;

    public BarWindow(
        EntryStore store,
        AppIconCache icons,
        WindowsClipboardWriter clipboard,
        SelectionCapture capture,
        AppSettings settings)
    {
        InitializeComponent();

        _store = store;
        _icons = icons;
        _clipboard = clipboard;
        _capture = capture;
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
        // Noted before this window takes the foreground, which it is about
        // to: pasting from a card has to land where the user was.
        _returnTo = ForegroundWindow.Current();

        ReloadIfBehind(force: true);
        _behindCheck.Start();
        Show();
        Activate();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>Hides with the standard fade, from a painted surface, and gives focus back.</summary>
    public void Dismiss()
    {
        _behindCheck.Stop();

        var fade = Motion.Fade(0);
        fade.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            Hide();
            _returnTo.Restore();
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
            KindText = entry.Kind == EntryKind.Image ? "图片" : "文本",
            WhenText = entry.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm"),
            Icon = _icons.For(entry.SourceApp),
            Thumbnail = entry.Kind == EntryKind.Image
                ? AppIconCache.Decode(entry.ThumbnailPng, 320)
                : null,
            OriginalPath = entry.OriginalPath,
            HasOriginal = entry.HasOriginal,
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
        EnsureActiveItem();
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

            var card = CardFor(entry);
            card.RowKeyText = BarKeys.RowKey(_pinned.Count + _cards.Count + 1);
            (pastPinned ? _cards : _pinned).Add(card);
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

    // --- hover actions --------------------------------------------------------

    /// <summary>Which of the user's chosen actions this card can honour, in their order.</summary>
    public IReadOnlyList<string> ActionsFor(BarCard card)
        => HoverActions.AvailableFor(
            HoverActions.Sanitise(_settings.BarActions), card.Kind, card.HasOriginal);

    private void OnCardMouseEnter(object sender, MouseEventArgs e)
    {
        if (Tree.FindDescendant<ActionTray>((DependencyObject)sender) is not { } tray
            || Tree.FindDescendant<HandoverText>((DependencyObject)sender) is not { } handover)
        {
            return;
        }

        handover.Yield();
        tray.Open();
    }

    private void OnCardMouseLeave(object sender, MouseEventArgs e)
    {
        if (Tree.FindDescendant<ActionTray>((DependencyObject)sender) is not { } tray
            || Tree.FindDescendant<HandoverText>((DependencyObject)sender) is not { } handover)
        {
            return;
        }

        tray.Close();
        handover.Return();
    }

    /// <summary>Runs one hover action. Invoked from any card's tray via the container's subscription.</summary>
    public void RunHoverAction(string id, BarCard card, Button button)
        => ExecuteAction(id, card, button);

    /// <summary>
    /// Executes one action. The keyboard uses this too, where there is no
    /// button to give feedback on.
    /// </summary>
    private void ExecuteAction(string id, BarCard card, Button? feedback)
    {
        switch (id)
        {
            case "copy":
                Confirm(feedback, _clipboard.SetText(card.Text));
                break;

            case "plain":
                // Stored text is plain by construction; this becomes distinct
                // when formatted entries exist (ticket 07).
                Confirm(feedback, _clipboard.SetText(card.Text));
                break;

            case "paste":
                PasteEntry(card);
                break;

            case "open":
                OpenOriginal(card);
                break;

            case "locate":
                LocateOriginal(card);
                break;

            case "pin":
                _store.SetPinned(card.Id, !card.IsPinned);
                ReloadData();
                break;

            case "delete":
                _store.Delete(card.Id);
                _browser.Forget(card.Id);
                RemoveCard(card);
                UpdateCount();
                EnsureActiveItem();
                break;
        }
    }

    /// <summary>
    /// Copies into the window the user was in before summoning the bar. The
    /// bar hides first — until it does, it is the thing in the way of the
    /// foreground the paste needs.
    /// </summary>
    private void PasteEntry(BarCard card)
    {
        if (!_returnTo.IsSomething)
        {
            _returnTo = ForegroundWindow.Current();
        }

        Hide();
        _returnTo.Restore();
        _capture.Paste(card.Text);
    }

    private void OpenOriginal(BarCard card)
    {
        if (card.OriginalPath is not { Length: > 0 } path)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // A file that has moved or gone missing since retention is not
            // worth interrupting the user over.
        }
    }

    private void LocateOriginal(BarCard card)
    {
        if (card.OriginalPath is not { Length: > 0 } path)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception)
        {
        }
    }

    private void ReloadData()
    {
        _browser.Reset();
        RefreshTagChoices();
        Rebuild();
    }

    private void RemoveCard(BarCard card)
    {
        var inRest = _cards.IndexOf(card);
        if (inRest >= 0)
        {
            _cards.RemoveAt(inRest);
        }
        else
        {
            _pinned.Remove(card);
        }

        if (_pinned.Count == 0)
        {
            PinnedHost.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// The success feedback: the pressed button becomes a tick for a second,
    /// and — only if the user asked — a sound joins it.
    /// </summary>
    private void Confirm(Button? button, bool succeeded)
    {
        if (button is null)
        {
            return;
        }

        if (!succeeded)
        {
            button.Content = "✗";
            return;
        }

        if (_settings.ActionSound)
        {
            System.Media.SystemSounds.Asterisk.Play();
        }

        var glyph = button.Content;
        button.Content = "✓";

        var restore = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        restore.Tick += (_, _) =>
        {
            restore.Stop();
            button.Content = glyph;
        };
        restore.Start();
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

    // --- keyboard model --------------------------------------------------------

    /// <summary>Rows as displayed: pinned first, then the rest. Number keys and navigation count these.</summary>
    private IEnumerable<BarCard> VisibleRows => _pinned.Concat(_cards);

    /// <summary>Focus sits in a text field: letters belong to it, arrows move its caret.</summary>
    private static bool IsTyping
        => Keyboard.FocusedElement is TextBox;

    /// <summary>
    /// There is always an active row while any row exists, so Enter and the
    /// letter actions always have something definite to act on.
    /// </summary>
    private void EnsureActiveItem()
    {
        if (_selected is { } card && (_cards.Contains(card) || _pinned.Contains(card)))
        {
            return;
        }

        Select(VisibleRows.FirstOrDefault());
    }

    private void Move(int delta)
    {
        var rows = VisibleRows.ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var current = _selected is { } card ? rows.IndexOf(card) : -1;
        var next = Math.Clamp(current + delta, 0, rows.Count - 1);

        Select(rows[next]);
        Cards.ScrollIntoView(rows[next]);
    }

    // --- key hints (ticket 14) -------------------------------------------------

    private bool _keyHintsOn;

    /// <summary>
    /// Hold Ctrl and every actionable icon swaps in place for the key that
    /// drives it — the whole keyboard model taught at the place it applies,
    /// for exactly as long as the user asks.
    /// </summary>
    private void SetKeyHints(bool on)
    {
        if (_keyHintsOn == on)
        {
            return;
        }

        _keyHintsOn = on;
        SearchKeyBadge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        KindKeyBadge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        TagKeyBadge.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        foreach (var container in RealizedContainers())
        {
            ApplyKeyHintsTo(
                Tree.FindDescendant<RowKeyBadge>(container),
                Tree.FindDescendant<ActionTray>(container));
        }
    }

    /// <summary>
    /// One row's hint state — also called for rows realized while Ctrl is
    /// already down, so recycled rows arrive pre-badged rather than blank.
    /// </summary>
    internal void ApplyKeyHintsTo(RowKeyBadge? badge, ActionTray? tray)
    {
        if (badge is not null)
        {
            badge.Visibility = _keyHintsOn ? Visibility.Visible : Visibility.Collapsed;
        }

        tray?.ShowHints(_keyHintsOn);
    }

    private IEnumerable<BarCardContainer> RealizedContainers()
    {
        foreach (var item in _pinned.Concat(_cards))
        {
            if (PinnedList.ItemContainerGenerator.ContainerFromItem(item) is BarCardContainer pinned)
            {
                yield return pinned;
            }

            if (Cards.ItemContainerGenerator.ContainerFromItem(item) is BarCardContainer rest)
            {
                yield return rest;
            }
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl && _keyHintsOn
            && (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            SetKeyHints(false);
        }
    }

    /// <summary>
    /// Losing focus while Ctrl is still down — Ctrl+Tab, a notification
    /// stealing the click — means the release event never arrives. The
    /// badges come in now, or they stay forever.
    /// </summary>
    private void OnLostFocus(object sender, EventArgs e) => SetKeyHints(false);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            SetKeyHints(true);
            return;
        }
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                StepEscape();
                break;

            case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                e.Handled = true;
                SearchBox.Focus();
                SearchBox.SelectAll();
                break;

            // Tab trades focus navigation for filter cycling: Ctrl+F is the
            // way back to the search box, so nothing is lost.
            case Key.Tab:
                e.Handled = true;
                CycleTag(+1);
                break;

            case Key.Left when !IsTyping:
                e.Handled = true;
                CycleKind(-1);
                break;

            case Key.Right when !IsTyping:
                e.Handled = true;
                CycleKind(+1);
                break;

            case Key.Up when Keyboard.FocusedElement is not ComboBox:
                e.Handled = true;
                Move(-1);
                break;

            case Key.Down when Keyboard.FocusedElement is not ComboBox:
                e.Handled = true;
                Move(+1);
                break;

            case Key.Enter:
                e.Handled = true;
                if (_selected is { } enter)
                {
                    // Modifier+Enter pastes as plain text; for stored text the
                    // two coincide until formatted entries exist (ticket 07).
                    PasteEntry(enter);
                }
                break;

            case Key.D1 or Key.NumPad1: NumberedRow(1, e); break;
            case Key.D2 or Key.NumPad2: NumberedRow(2, e); break;
            case Key.D3 or Key.NumPad3: NumberedRow(3, e); break;
            case Key.D4 or Key.NumPad4: NumberedRow(4, e); break;
            case Key.D5 or Key.NumPad5: NumberedRow(5, e); break;
            case Key.D6 or Key.NumPad6: NumberedRow(6, e); break;
            case Key.D7 or Key.NumPad7: NumberedRow(7, e); break;
            case Key.D8 or Key.NumPad8: NumberedRow(8, e); break;
            case Key.D9 or Key.NumPad9: NumberedRow(9, e); break;
            case Key.D0 or Key.NumPad0: NumberedRow(10, e); break;

            // Letter actions only outside text fields — inside one, letters
            // are the search the user is typing.
            case Key.C when !IsTyping && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                if (_selected is { } copy) ExecuteAction("copy", copy, feedback: null);
                break;

            case Key.O when !IsTyping && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                if (_selected is { } open) ExecuteAction("open", open, feedback: null);
                break;

            case Key.P when !IsTyping && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                if (_selected is { } pin) ExecuteAction("pin", pin, feedback: null);
                break;

            case Key.D when !IsTyping && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                if (_selected is { } del) ExecuteAction("delete", del, feedback: null);
                break;
        }
    }

    private void NumberedRow(int oneBased, KeyEventArgs e)
    {
        var rows = VisibleRows.ToList();

        if (oneBased <= rows.Count)
        {
            e.Handled = true;
            PasteEntry(rows[oneBased - 1]);
        }
    }

    /// <summary>Escape peels the most recent layer; only an empty stack hides the window.</summary>
    private void StepEscape()
    {
        var hasTag = TagFilter.SelectedItem as string is { } tag && tag != AnyTag;
        var hasKind = KindFilter.SelectedIndex != 0;

        switch (BarKeyboard.NextEscape(previewOpen: false, hasTag, hasKind))
        {
            case BarKeyboard.EscapeAction.ClosePreview:
                // Preview arrives with ticket 17's window; the level is
                // already part of the stack.
                break;

            case BarKeyboard.EscapeAction.ClearTagFilter:
                TagFilter.SelectedItem = AnyTag;
                break;

            case BarKeyboard.EscapeAction.ClearTypeFilter:
                KindFilter.SelectedIndex = 0;
                break;

            case BarKeyboard.EscapeAction.HideWindow:
                Dismiss();
                break;
        }
    }

    private void CycleKind(int delta)
    {
        KindFilter.SelectedIndex = BarKeyboard.Cycle(KindFilter.SelectedIndex, delta, KindFilter.Items.Count);
    }

    private void CycleTag(int delta)
    {
        var index = TagFilter.Items.IndexOf(TagFilter.SelectedItem);
        var next = BarKeyboard.Cycle(index < 0 ? 0 : index, delta, TagFilter.Items.Count);
        TagFilter.SelectedIndex = next;
    }
}
