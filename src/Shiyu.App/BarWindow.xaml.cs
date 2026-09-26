using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>
/// A card's text body, typed so the window can find it in a realized row —
/// among other things to dress it as a link while Ctrl is held.
/// </summary>
internal sealed class EntryBodyText : TextBlock
{
}

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
    private BarCard? _subscribed;

    public BarCardContainer()
    {
        // Recycling in a nutshell: the same container instance gets a new
        // row's DataContext.
        DataContextChanged += (_, _) => PrepareTray();

        // First realization has a hole the DataContext event cannot cover: it
        // fires before the template applies, when the tray does not exist in
        // the tree yet. Loaded fires once the visual tree is in place, which
        // is the first moment the tray can actually be configured. Without
        // this retry, every freshly realized row ends up with an empty tray.
        Loaded += (_, _) => PrepareTray();
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

        if (!ReferenceEquals(card, _subscribed))
        {
            if (_subscribed is not null)
            {
                _subscribed.PropertyChanged -= OnCardChanged;
            }

            _subscribed = card;
            card.PropertyChanged += OnCardChanged;
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

        // Reconfigured while the pointer never left (favouriting flips delete
        // protection): the tray reopens rather than snapping shut under the
        // cursor waiting for an enter that will not come.
        if (IsMouseOver)
        {
            tray.Open();
        }
    }

    /// <summary>
    /// Favouriting toggles delete protection, and the tray's actions are
    /// chosen when the row was built — without this, a card starred a second
    /// ago keeps showing the delete button the promise says is gone.
    /// </summary>
    private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BarCard.Favorite))
        {
            PrepareTray();
        }
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

internal static class BrushExtensions
{
    /// <summary>A brush frozen for sharing across recycled rows.</summary>
    public static Brush FrozenBrush(this SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// A window's rectangle in physical pixels, read straight from the OS — the
/// one source that is right on every monitor and scale combination.
/// </summary>
internal static class WindowRects
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static bool TryGet(IntPtr handle, out ScreenRect rect)
    {
        if (handle != IntPtr.Zero && GetWindowRect(handle, out var native))
        {
            rect = new ScreenRect(native.Left, native.Top, native.Right, native.Bottom);
            return true;
        }

        rect = default;
        return false;
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

    /// <summary>How to drag out, shown under the preview in the tooltip.</summary>
    public string DragHint { get; init; } = string.Empty;

    /// <summary>The card's tooltip: its content, and what holding it does.</summary>
    public string DragToolTip => string.IsNullOrEmpty(Preview)
        ? DragHint
        : Preview + Environment.NewLine + Environment.NewLine + DragHint;

    public string KindText { get; init; } = string.Empty;

    public string WhenText { get; init; } = string.Empty;

    /// <summary>The absolute stamp (and usage count) behind the relative one.</summary>
    public string WhenToolTip { get; init; } = string.Empty;

    /// <summary>
    /// The number key that pastes this row, when it is one of the first ten
    /// displayed rows; null otherwise. Assigned from display position.
    /// </summary>
    public string? RowKeyText { get; set; }

    public ImageSource? Icon { get; init; }

    public ImageSource? Thumbnail { get; init; }

    public string? OriginalPath { get; init; }

    public bool HasOriginal { get; init; }

    public EntrySubtype Subtype { get; init; }

    /// <summary>The copy's HTML form, kept so pasting back into a rich destination keeps its formatting.</summary>
    public string? Html { get; init; }

    public string? Rtf { get; init; }

    /// <summary>The file rows a file card shows, already clamped to the density knob.</summary>
    public IReadOnlyList<FileRow> FileRows { get; init; } = [];

    /// <summary>A file copy made entirely of images previews its first file.</summary>
    public ImageSource? FilePreviewSource { get; init; }

    /// <summary>True when this entry is Shiyu's own kept translation of another.</summary>
    public bool IsTranslation { get; init; }

    public Visibility TranslationVisibility => IsTranslation
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility FilePreviewVisibility => FilePreviewSource is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    /// <summary>The full capped path list of a file entry, for copying and pasting back.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    private bool _favorite;

    /// <summary>Mutates in place: favouriting must not disturb the list around it.</summary>
    public bool Favorite
    {
        get => _favorite;
        set
        {
            _favorite = value;
            Changed(nameof(Favorite));
            // The watermark binds the visibility, not the flag — without this
            // the star never appears, and un-favouriting looks impossible.
            Changed(nameof(FavoriteVisibility));
        }
    }

    public Visibility FavoriteVisibility => Favorite ? Visibility.Visible : Visibility.Collapsed;

    private string? _groupBadge;

    /// <summary>
    /// The name of the pile this entry was filed into. Mutates in place, like
    /// the favourite flag: filing is organisation, not reordering.
    /// </summary>
    public string? GroupBadge
    {
        get => _groupBadge;
        set
        {
            _groupBadge = value;
            Changed(nameof(GroupBadge));
            Changed(nameof(GroupBadgeVisibility));
        }
    }

    public Visibility GroupBadgeVisibility => string.IsNullOrEmpty(GroupBadge)
        ? Visibility.Collapsed
        : Visibility.Visible;

    private string? _note;

    /// <summary>Mutates in place: the note becomes the entry's public face the moment it is saved.</summary>
    public string? Note
    {
        get => _note;
        set
        {
            _note = value;
            Changed(nameof(Note));
        }
    }

    private string _face = string.Empty;

    /// <summary>What the body shows: the note by default, the original on hover.</summary>
    public string Face
    {
        get => _face;        set
        {
            _face = value;
            Changed(nameof(Face));
        }
    }

    /// <summary>True when every path of a file entry is gone — the card shows it struck through and faded.</summary>
    public bool AllPathsDead { get; init; }

    public int FileCount { get; init; }

    public Visibility FilesVisibility =>
        Kind == EntryKind.Files ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The quiet count line under the file rows.</summary>
    public string FileTail => FileCount switch
    {
        0 => string.Empty,
        1 => "1 个项目",
        _ => $"共 {FileCount} 项",
    };

    /// <summary>Links and emails open in the system's default program.</summary>
    public bool IsOpenable => Subtype is EntrySubtype.Link or EntrySubtype.Email;

    /// <summary>The parsed colour of a colour entry, as a frozen brush ready to paint.</summary>
    public Brush? SwatchBrush { get; init; }

    public Visibility SwatchVisibility =>
        SwatchBrush is null ? Visibility.Collapsed : Visibility.Visible;

    public int TextLines { get; init; }

    /// <summary>
    /// The text clamp as a height of whole Body lines — visually identical to
    /// MaxLines, usable from XAML on this build (see the template comment).
    /// </summary>
    public double TextMaxHeight
        => TextLines * DesignTokens.LineHeightFor(DesignTokens.FontBody);

    public int ImageHeight { get; init; }

    /// <summary>The original image's pixel size, for the preview panel's pre-computed shape.</summary>
    public int PixelWidth { get; init; }

    public int PixelHeight { get; init; }

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

/// <summary>One row of a file card: a name, its type icon, and whether the path still exists.</summary>
internal sealed record FileRow(string Name, string FullPath, ImageSource? Icon, bool Dead)
{
    public Visibility DeadVisibility => Dead ? Visibility.Visible : Visibility.Collapsed;
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
    private readonly FileTypeIcons _fileIcons;
    private readonly HistoryBrowser _browser;
    private readonly ObservableCollection<BarCard> _pinned = [];
    private readonly ObservableCollection<BarCard> _cards = [];
    private readonly System.Windows.Threading.DispatcherTimer _searchDebounce;
    private readonly System.Windows.Threading.DispatcherTimer _behindCheck;

    // --- preview panel (ticket 17) ------------------------------------------------
    // The policy decides when the preview opens, follows, and closes; this
    // window supplies the events and owns the timers. One repeating tick
    // drives every time-based decision, so there are no drifting timers.

    private PreviewWindow? _preview;

    private PreviewPolicy _previewPolicy = new(500, () => Environment.TickCount64);

    private readonly System.Windows.Threading.DispatcherTimer _previewTick;

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
        AppSettings settings,
        FileTypeIcons fileIcons)
    {
        InitializeComponent();

        _store = store;
        _icons = icons;
        _clipboard = clipboard;
        _capture = capture;
        _settings = settings;
        _fileIcons = fileIcons;
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

        _previewPolicy = new PreviewPolicy(settings.PreviewHoverDelayMs, () => Environment.TickCount64);
        _previewTick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _previewTick.Tick += (_, _) => RunPreviewCommand(_previewPolicy.Tick());

        // Win11 material behind the sheet (ticket 30): the window went layered
        // in XAML, which is the only surface the backdrop renders on.
        Backdrop.Attach(this, () => BackdropKind.Acrylic);

        PinnedList.ItemsSource = _pinned;
        Cards.ItemsSource = _cards;

        RestoreGeometry();
        RefreshTagChoices();
        RefreshGroups();
        Rebuild();
    }

    /// <summary>New settings apply on the spot: density clamps change, hotkey label follows.</summary>
    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;

        // A changed dwell or a disabled hover takes effect on the next event;
        // a preview already up keeps its own rules until it closes.
        _previewPolicy = new PreviewPolicy(settings.PreviewHoverDelayMs, () => Environment.TickCount64);
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
        _previewTick.Start();
        MoveBesideCursorIfWanted();
        Show();
        Activate();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>
    /// Like the system's Win+V panel: appear where the user is, not where the
    /// window was last left. Off, the remembered geometry stands — a bar that
    /// always comes back to the same place is also a place the user learns.
    /// </summary>
    private void MoveBesideCursorIfWanted()
    {
        if (!_settings.BarAtCursor)
        {
            return;
        }

        var cursor = ScreenGeometry.CursorPosition();
        var workArea = ScreenGeometry.WorkAreaAt(cursor);

        // The scale comes from the monitor itself (GetDpiForMonitor), not from
        // WPF: on the first summon the PresentationSource does not exist yet,
        // and its silent 1.0 fallback made Place see DIU-sized dimensions —
        // no flip, no clamp, the bar's real bottom off the work area (ticket
        // 30's screenshot probe).
        var (scaleX, scaleY) = ScreenGeometry.ScaleAt(cursor);

        // Declared Width/Height, never Actual*: nothing has been laid out on
        // the first summon, so the Actual values are 0.
        var width = Width;
        var height = Math.Min(Height, workArea.Height / scaleY);
        var placed = BadgePlacement.Place(
            cursor,
            (int)Math.Ceiling(width * scaleX),
            (int)Math.Ceiling(height * scaleY),
            workArea);

        // The very first summon happens before the window was ever shown, when
        // the handle does not exist yet — asking for it creates the HWND.
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        _ = helper.EnsureHandle();
        TransientWindow.MoveTo(helper.Handle, placed);
    }

    /// <summary>Hides with the standard fade, from a painted surface, and gives focus back.</summary>
    public void Dismiss()
    {
        _behindCheck.Stop();
        _previewTick.Stop();
        RunPreviewCommand(_previewPolicy.BarHidden());

        var fade = Motion.Fade(0);
        fade.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            Hide();
            _returnTo.Restore();
            EnterLightweightIfEnabled();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// While hidden, the realised cards are the whole cost of the window —
    /// thumbnails decoded, rows laid out — and none of it is doing anything.
    /// Dropping them and trimming the working set costs a rebuild on the next
    /// summon, which ReloadIfBehind(force) already does; recording never
    /// pauses, because the listener and the pipeline do not live here.
    /// </summary>
    private void EnterLightweightIfEnabled()
    {
        if (!_settings.LightweightWhenHidden || IsVisible)
        {
            return;
        }

        _pinned.Clear();
        _cards.Clear();
        _selected = null;

        GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);
        MemoryTrim.WorkingSet();
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

        var shown = Math.Max(1, _settings.BarFileCount);
        var fileRows = entry.Kind == EntryKind.Files
            ? entry.Files.Take(shown)
                .Select(path => new FileRow(
                    Path.GetFileName(path) is { Length: > 0 } name ? name : path,
                    path,
                    _fileIcons.For(path),
                    !File.Exists(path)))
                .ToList()
            : [];

        var card = new BarCard
        {
            Id = entry.Id,
            Kind = entry.Kind,
            Text = entry.Text,
            Preview = collapsed.Length > 500 ? collapsed[..500] + "…" : collapsed,
            DragHint = "🖱 按住左键拖出：文本入编辑器（带格式）、图片入聊天窗、文件入资源管理器",
            KindText = entry.Kind switch
            {
                EntryKind.Image => "图片",
                EntryKind.Files => "文件",
                _ => entry.TranslatedFrom is null ? "文本" : "译文 · 译自原文",
            },

            // Relative for the scan, absolute (plus the usage count, which is
            // a reward rather than an identity) for the hover.
            WhenText = RelativeTime.For(entry.CreatedAt, DateTimeOffset.Now),
            WhenToolTip = $"{entry.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}"
                + (entry.UseCount > 0 ? $"\n用过 {entry.UseCount} 次" : ""),
            Icon = _icons.For(entry.SourceApp),
            Thumbnail = entry.Kind == EntryKind.Image
                ? AppIconCache.Decode(entry.ThumbnailPng, 320)
                : null,
            OriginalPath = entry.OriginalPath,
            HasOriginal = entry.HasOriginal,
            Subtype = entry.Subtype,
            Html = entry.Html,
            Rtf = entry.Rtf,
            Files = entry.Files,
            Favorite = entry.Favorite,
            Note = entry.Note,
            GroupBadge = entry.GroupId is { } filedInto && _groupsById.TryGetValue(filedInto, out var pile)
                ? pile.Name
                : null,
            IsTranslation = entry.TranslatedFrom is not null,
            FileRows = fileRows,
            FilePreviewSource = PreviewFileImage(entry),
            AllPathsDead = entry.Kind == EntryKind.Files && entry.Files.All(path => !File.Exists(path)),
            FileCount = entry.Files.Count,
            SwatchBrush = entry.Subtype == EntrySubtype.Color
                && SubtypeColor.TryParse(entry.Text, out var colour)
                ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(colour.A, colour.R, colour.G, colour.B))
                    .FrozenBrush()
                : null,
            TextLines = Math.Max(1, _settings.BarTextLines),
            ImageHeight = Math.Max(24, _settings.BarImageHeight),
            PixelWidth = entry.ImageWidth,
            PixelHeight = entry.ImageHeight,
            IsPinned = entry.IsPinned,
        };

        // The note is the public face; the original waits behind a hover.
        card.Face = entry.Note is { Length: > 0 } ? entry.Note : card.Preview;
        return card;
    }

    /// <summary>
    /// A file copy made entirely of images previews as pictures: names alone
    /// answer "which file", not "what was in it". A missing or unreadable file
    /// falls back to the rows — a struck-through name says more than nothing.
    /// </summary>
    private static ImageSource? PreviewFileImage(Entry entry)
    {
        if (entry.Kind != EntryKind.Files
            || FileEntries.PreviewImagePath(entry.Files) is not { } path
            || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 320;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            // A file with an image extension that is not a readable bitmap.
            return null;
        }
    }
    private void Rebuild()
    {
        _pinned.Clear();
        _cards.Clear();
        _selected = null;
        Append(_browser.Loaded);
        PinnedHost.Visibility = _pinned.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCount();
        UpdateEmptyState();
        EnsureActiveItem();
    }

    /// <summary>
    /// The empty panel says which conditions emptied the list — a filter that
    /// ate everything is recoverable, and the words are the recovery map.
    /// </summary>
    private void UpdateEmptyState()
    {
        var empty = _pinned.Count == 0 && _cards.Count == 0;
        EmptyHost.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        if (!empty)
        {
            return;
        }

        var groupName = _selectedGroup is { } picked && _groupsById.TryGetValue(picked, out var group)
            ? group.Name
            : null;

        var copy = EmptyStates.For(
            new HistoryFilter
            {
                Query = SearchBox.Text,
                Favorite = FavoriteOnly.IsChecked == true ? true : null,
                Kind = KindFilter.SelectedIndex switch
                {
                    1 => EntryKind.Text,
                    2 => EntryKind.Image,
                    3 => EntryKind.Files,
                    _ => null,
                },
                Group = _selectedGroup,
            },
            groupName,
            _store.Count());

        EmptyHeadline.Text = copy.Headline;
        EmptyHint.Text = copy.Hint;
        EmptyClear.Visibility = copy.OfferClear ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>
    /// The footer's three voices: the count (total, or "3 / 161" when a filter
    /// is on — a paged list cannot count itself), the action feedback, and the
    /// Esc hint, which follows the escape stack instead of asserting one
    /// behaviour while another is in force.
    /// </summary>
    private void UpdateFooter()
    {
        if (_feedbackHostOpen)
        {
            return;
        }

        var total = _store.Count();
        var filtered = _store.CountMatching(_browser.Filter);
        var escHint = _browser.Filter.IsEmpty ? "Esc 隐藏" : "Esc 清除筛选";

        CountLabel.Text = filtered == total
            ? $"共 {total} 条 · {escHint}"
            : $"{filtered} / {total} 条 · {escHint}";
    }

    private void UpdateCount() => UpdateFooter();

    /// <summary>Whether a feedback row is on show (and owns the footer).</summary>
    private bool _feedbackHostOpen;

    private System.Windows.Threading.DispatcherTimer? _feedbackTimer;

    private IReadOnlyList<(Entry Entry, string? Group)>? _undoPending;

    /// <summary>
    /// One feedback row at a time; an undo keeps it on show for the full five
    /// seconds the user was promised, a plain confirmation leaves quickly.
    /// </summary>
    private void ShowFeedback(string text, IReadOnlyList<(Entry Entry, string? Group)>? undo = null)
    {
        _feedbackTimer?.Stop();
        _feedbackHostOpen = true;
        _undoPending = undo;

        FeedbackLabel.Text = text;
        UndoButton.Visibility = undo is null ? Visibility.Collapsed : Visibility.Visible;
        FeedbackHost.Visibility = Visibility.Visible;
        CountLabel.Visibility = Visibility.Collapsed;

        _feedbackTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = undo is null ? TimeSpan.FromSeconds(1.6) : TimeSpan.FromSeconds(5),
        };
        _feedbackTimer.Tick += (_, _) => ClearFeedback();
        _feedbackTimer.Start();
    }

    private void ClearFeedback()
    {
        _feedbackTimer?.Stop();
        _feedbackTimer = null;
        _feedbackHostOpen = false;
        _undoPending = null;
        FeedbackHost.Visibility = Visibility.Collapsed;
        CountLabel.Visibility = Visibility.Visible;
        UpdateFooter();
    }

    private void OnUndoDelete(object sender, RoutedEventArgs e) => RestoreUndo();

    private void RestoreUndo()
    {
        if (_undoPending is not { } items)
        {
            return;
        }

        foreach (var (entry, group) in items)
        {
            // Re-filing by group name recreates the group if it went away
            // mid-window, which is the same promise delete made about entries.
            _store.ImportEntry(entry, group);
        }

        ClearFeedback();
        ApplyFilter();
        ShowFeedback($"已恢复 {items.Count} 条");
    }

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
        // Scrolling says the user has moved on: a hover preview goes, a
        // held-space preview answers to its key alone.
        RunPreviewCommand(_previewPolicy.Scrolled());

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
        SubtypeFilter.SelectedIndex = 0;
        FavoriteOnly.IsChecked = false;
        SelectGroup(null, apply: false);
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
                3 => EntryKind.Files,
                _ => null,
            },
            Subtype = SubtypeFilter.SelectedIndex switch
            {
                1 => EntrySubtype.Link,
                2 => EntrySubtype.Email,
                3 => EntrySubtype.Color,
                4 => EntrySubtype.LocalPath,
                _ => null,
            },
            Favorite = FavoriteOnly.IsChecked == true ? true : null,
            Group = _selectedGroup,
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

    // --- groups -----------------------------------------------------------------

    /// <summary>The drawer currently open, or null when the bar shows every pile at once.</summary>
    private long? _selectedGroup;

    private readonly Dictionary<long, EntryGroup> _groupsById = [];

    /// <summary>Chips for each visible group, in row order; the overflow takes the rest.</summary>
    private readonly List<(EntryGroup Group, ToggleButton Chip)> _groupChips = [];

    private BarCard? _chooserCard;

    private Popup? _groupChooser;

    private bool _fittingGroups;

    /// <summary>Carries a settings deep-link id up to whoever owns settings.</summary>
    public event Action<string>? DataSettingsRequested;

    /// <summary>Rebuilds the switcher row after groups appear, change, or vanish.</summary>
    private void RefreshGroups()
    {
        var groups = _store.Groups();
        _groupsById.Clear();
        foreach (var group in groups)
        {
            _groupsById[group.Id] = group;
        }

        // A deleted group leaves its id selected on the floor; back to 全部.
        if (_selectedGroup is { } current && !_groupsById.ContainsKey(current))
        {
            _selectedGroup = null;
        }

        foreach (var (_, chip) in _groupChips)
        {
            GroupRow.Children.Remove(chip);
        }
        _groupChips.Clear();

        foreach (var group in groups.Where(group => !group.Hidden))
        {
            var chip = new ToggleButton
            {
                Content = $"{group.Icon ?? "组"} {group.Name}",
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = FindResource("Size.Caption") as double? ?? 12,
                Cursor = Cursors.Hand,
                ToolTip = $"只看「{group.Name}」——再点一次回到全部",
            };
            chip.Click += OnGroupChipClicked;
            GroupRow.Children.Insert(GroupRow.Children.Count - 1, chip);
            _groupChips.Add((group, chip));
        }

        GroupHost.Visibility = groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SyncGroupChipStates();
        FitGroupRow();
    }

    private void OnGroupChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton chip)
        {
            return;
        }

        // Clicking the chip that is already active un-drawers: back to 全部.
        var picked = _groupChips.FirstOrDefault(pair => pair.Chip == chip).Group?.Id;
        SelectGroup(chip.IsChecked == true ? picked : null);
    }

    private void OnGroupHostResized(object sender, SizeChangedEventArgs e) => FitGroupRow();

    /// <summary>
    /// Shows as many chips as the row has room for; the rest fold into the
    /// overflow button, which itself lights up when the active drawer is one
    /// of them — a selection the user cannot see is a selection they cannot
    /// trust.
    /// </summary>
    private void FitGroupRow()
    {
        if (_fittingGroups || GroupHost.Visibility != Visibility.Visible)
        {
            return;
        }

        _fittingGroups = true;
        try
        {
            var available = GroupHost.ActualWidth;
            if (available <= 0)
            {
                return;
            }

            AllGroupChip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            available -= AllGroupChip.DesiredSize.Width + 6;

            GroupOverflow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var overflowWidth = GroupOverflow.DesiredSize.Width + 6;

            var used = 0.0;
            var fitted = 0;
            foreach (var (_, chip) in _groupChips)
            {
                chip.Visibility = Visibility.Visible;
                chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var width = chip.DesiredSize.Width + 6;

                // Room for the overflow button is reserved up front unless
                // this is the last chip: better to show one chip fewer than
                // to squeeze the ⋯ button out of the row.
                var reserve = fitted + 1 < _groupChips.Count ? overflowWidth : 0;

                if (used + width + reserve > available)
                {
                    break;
                }

                used += width;
                fitted++;
            }

            var overflowed = _groupChips.Skip(fitted).ToList();
            foreach (var (_, chip) in overflowed)
            {
                chip.Visibility = Visibility.Collapsed;
            }

            GroupOverflow.Visibility = overflowed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BuildOverflowChoices(overflowed.Select(pair => pair.Group).ToList());
            SyncGroupChipStates();
        }
        finally
        {
            _fittingGroups = false;
        }
    }

    private Popup? _overflowPopup;

    private void OnGroupOverflowClicked(object sender, RoutedEventArgs e)
    {
        if (_overflowPopup is null)
        {
            return;
        }

        _overflowPopup.IsOpen = !_overflowPopup.IsOpen;
    }

    private void BuildOverflowChoices(IReadOnlyList<EntryGroup> overflowed)
    {
        if (_overflowPopup is not null)
        {
            _overflowPopup.IsOpen = false;
        }

        var host = new StackPanel { MinWidth = 120 };

        foreach (var group in overflowed)
        {
            var item = new Button
            {
                Content = $"{group.Icon ?? "组"} {group.Name}",
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = Cursors.Hand,
                Tag = group.Id,
            };
            item.SetResourceReference(BackgroundProperty, "Brush.Surface");
            item.Click += (_, _) =>
            {
                _overflowPopup!.IsOpen = false;
                SelectGroup(group.Id);
            };
            host.Children.Add(item);
        }

        var manage = new Button
        {
            Content = "管理分组…",
            Padding = new Thickness(10, 5, 10, 5),
            Cursor = Cursors.Hand,
        };
        manage.SetResourceReference(BackgroundProperty, "Brush.Surface");
        manage.Click += OnManageGroups;
        host.Children.Add(manage);

        _overflowPopup = new Popup
        {
            Child = host,
            PlacementTarget = GroupOverflow,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
        };

        // Rebinding after rebuild keeps the anchor when the row re-fits while open.
        _overflowPopup.IsOpen = false;
    }

    /// <summary>Switches the drawer. The chips show it; the filter narrows to it.</summary>
    private void SelectGroup(long? id, bool apply = true)
    {
        _selectedGroup = id;
        SyncGroupChipStates();

        if (apply)
        {
            ApplyFilter();
        }
    }

    private void SyncGroupChipStates()
    {
        AllGroupChip.IsChecked = _selectedGroup is null;

        var selectedIsOverflowed = false;
        foreach (var (group, chip) in _groupChips)
        {
            var active = group.Id == _selectedGroup;
            chip.IsChecked = active;
            if (active && chip.Visibility != Visibility.Visible)
            {
                selectedIsOverflowed = true;
            }
        }

        // The overflow button speaks for the drawer hidden inside it.
        GroupOverflow.SetResourceReference(BorderBrushProperty, selectedIsOverflowed
            ? "Brush.Accent"
            : "Brush.Border");
        GroupOverflow.Foreground = selectedIsOverflowed
            ? FindResource("Brush.Accent") as Brush
            : null;
    }

    /// <summary>The 归组 tray action: file this card into a pile, or back out of one.</summary>
    private void OpenGroupChooser(BarCard card, Button? anchor)
    {
        _chooserCard = card;

        var host = new StackPanel { MinWidth = 150 };

        foreach (var group in _store.Groups())
        {
            var item = new Button
            {
                Content = $"{group.Icon ?? "组"} {group.Name}",
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = Cursors.Hand,
                Tag = group.Id,
            };
            item.SetResourceReference(BackgroundProperty, "Brush.Surface");
            item.Click += (_, _) => FileCardInto(card, group.Id);
            host.Children.Add(item);
        }

        var ungrouped = new Button
        {
            Content = "未分组",
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.Hand,
        };
        ungrouped.SetResourceReference(BackgroundProperty, "Brush.Surface");
        ungrouped.Click += (_, _) => FileCardInto(card, null);
        host.Children.Add(ungrouped);

        var name = new TextBox { Margin = new Thickness(0, 0, 0, 2), Padding = new Thickness(6, 4, 6, 4) };
        name.SetResourceReference(BackgroundProperty, "Brush.SurfaceInput");
        host.Children.Add(name);

        var create = new Button
        {
            Content = "新建分组并归入",
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 0, 2),
            Cursor = Cursors.Hand,
        };
        create.SetResourceReference(BackgroundProperty, "Brush.Surface");
        create.Click += (_, _) =>
        {
            var label = name.Text.Trim();
            if (label.Length == 0)
            {
                return;
            }

            var icon = label.FirstOrDefault(char.IsLetterOrDigit).ToString();
            var id = _store.CreateGroup(label, icon);
            RefreshGroups();
            FileCardInto(card, id);
        };
        host.Children.Add(create);

        var manage = new Button { Content = "管理分组…", Padding = new Thickness(10, 5, 10, 5), Cursor = Cursors.Hand };
        manage.SetResourceReference(BackgroundProperty, "Brush.Surface");
        manage.Click += OnManageGroups;
        host.Children.Add(manage);

        _groupChooser = new Popup
        {
            Child = host,
            PlacementTarget = anchor,
            Placement = anchor is null ? PlacementMode.MousePoint : PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
        };
        _groupChooser.IsOpen = true;
    }

    private void FileCardInto(BarCard card, long? groupId)
    {
        _store.SetEntryGroup(card.Id, groupId);
        card.GroupBadge = groupId is { } id && _groupsById.TryGetValue(id, out var group)
            ? group.Name
            : null;

        if (_groupChooser is not null)
        {
            _groupChooser.IsOpen = false;
        }

        // A drawer the card just left should not keep showing it; one it
        // joined does not gain it until asked, which the switcher already is.
        if (_selectedGroup is { } selected && selected != groupId)
        {
            ApplyFilter();
        }
    }

    private void OnManageGroups(object sender, RoutedEventArgs e)
    {
        if (_overflowPopup is not null)
        {
            _overflowPopup.IsOpen = false;
        }

        if (_groupChooser is not null)
        {
            _groupChooser.IsOpen = false;
        }

        var manager = new GroupManagerWindow(_store) { Owner = this };
        manager.DataSettingsRequested += () => DataSettingsRequested?.Invoke("store.protect");
        manager.ShowDialog();
        RefreshGroups();
        ApplyFilter();
    }

    // --- card context menu ----------------------------------------------------

    private Popup? _cardMenu;

    private Popup? _cardSubMenu;

    /// <summary>
    /// The right-click menu is a Popup the app renders itself, not a system
    /// ContextMenu: a system menu takes focus, and a bar that never activates
    /// can find itself hidden once the menu closes — the user's action would
    /// die half-done. A Popup never activates anything.
    /// </summary>
    private void OnCardRightClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not BarCard card)
        {
            return;
        }

        e.Handled = true;
        Select(card);
        CloseCardMenu();
        OpenCardMenu(card, (FrameworkElement)sender, e.GetPosition((IInputElement)sender));
    }

    private void OpenCardMenu(BarCard card, FrameworkElement anchor, Point at)
    {
        var panel = new StackPanel { MinWidth = 172 };

        foreach (var action in ActionsFor(card))
        {
            // 归组 opens its own submenu here — the tray button opens the
            // chooser popup instead; a menu is where a submenu belongs.
            if (action == "group")
            {
                panel.Children.Add(GroupSubmenuItem(card));
                continue;
            }

            var captured = action;
            panel.Children.Add(MenuRow(
                HoverActions.Name(action),
                ShortcutFor(action),
                HoverActions.IsDestructive(captured),
                () =>
                {
                    CloseCardMenu();
                    ExecuteAction(captured, card, feedback: null);
                }));
        }

        _cardMenu = new Popup
        {
            Child = MenuSurface(panel),
            PlacementTarget = anchor,
            Placement = PlacementMode.RelativePoint,
            PlacementRectangle = new Rect(at.X, at.Y, 0, 0),
            StaysOpen = false,
            AllowsTransparency = true,
        };
        _cardMenu.Opened += (_, _) => FlipIntoWorkArea(_cardMenu);
        _cardMenu.IsOpen = true;
    }

    private void CloseCardMenu()
    {
        if (_cardSubMenu is not null)
        {
            _cardSubMenu.IsOpen = false;
            _cardSubMenu = null;
        }

        if (_cardMenu is not null)
        {
            _cardMenu.IsOpen = false;
            _cardMenu = null;
        }
    }

    /// <summary>What the menu shows next to an action: the key that runs it.</summary>
    private static string ShortcutFor(string action) => action switch
    {
        "paste" => "Enter",
        _ => BarKeys.TrayKey(action) ?? string.Empty,
    };

    private UIElement MenuRow(string label, string shortcut, bool danger, Action run)
    {
        var name = new TextBlock { Text = label };
        if (danger)
        {
            name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        }

        var key = new TextBlock { Text = shortcut, MinWidth = 26, TextAlignment = TextAlignment.Right };
        key.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        DockPanel.SetDock(key, Dock.Right);

        var content = new DockPanel();
        content.Children.Add(key);
        content.Children.Add(name);

        var row = new Button
        {
            Content = content,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 0, 1),
            Cursor = Cursors.Hand,
        };
        row.Click += (_, _) => run();
        return row;
    }

    /// <summary>The one second-level menu: filing the card into a group.</summary>
    private UIElement GroupSubmenuItem(BarCard card)
    {
        var label = new TextBlock { Text = "归组" };
        var arrow = new TextBlock { Text = "▸", MinWidth = 26, TextAlignment = TextAlignment.Right };
        arrow.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        DockPanel.SetDock(arrow, Dock.Right);

        var content = new DockPanel();
        content.Children.Add(arrow);
        content.Children.Add(label);

        var row = new Button
        {
            Content = content,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 0, 1),
            Cursor = Cursors.Hand,
        };

        row.MouseEnter += (_, _) =>
        {
            if (_cardSubMenu is not null)
            {
                _cardSubMenu.IsOpen = false;
            }

            var list = new StackPanel { MinWidth = 140 };
            foreach (var group in _store.Groups())
            {
                var captured = group;
                var item = new Button
                {
                    Content = $"{group.Icon ?? "组"} {group.Name}",
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 0, 1),
                    Cursor = Cursors.Hand,
                };
                item.Click += (_, _) =>
                {
                    CloseCardMenu();
                    FileCardInto(card, captured.Id);
                };
                list.Children.Add(item);
            }

            var ungrouped = new Button
            {
                Content = "未分组",
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = Cursors.Hand,
            };
            ungrouped.Click += (_, _) =>
            {
                CloseCardMenu();
                FileCardInto(card, null);
            };
            list.Children.Add(ungrouped);

            var manage = new Button { Content = "管理分组…", Padding = new Thickness(10, 5, 10, 5), Cursor = Cursors.Hand };
            manage.Click += (_, _) =>
            {
                CloseCardMenu();
                OnManageGroups(this, new RoutedEventArgs());
            };
            list.Children.Add(manage);

            _cardSubMenu = new Popup
            {
                Child = MenuSurface(list),
                PlacementTarget = row,
                Placement = PlacementMode.Right,
                StaysOpen = false,
                AllowsTransparency = true,
            };
            _cardSubMenu.IsOpen = true;
        };

        return row;
    }

    private static Border MenuSurface(StackPanel panel)
    {
        var border = new Border
        {
            Child = panel,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(6),
        };
        border.SetResourceReference(BackgroundProperty, "Brush.Surface");
        border.SetResourceReference(BorderBrushProperty, "Brush.Border");
        border.BorderThickness = new Thickness(1);
        return border;
    }

    /// <summary>
    /// A menu that would hang off the screen edge is nudged back in — measured
    /// in physical pixels against the work area of the monitor it is on, and
    /// shifted in device-independent units.
    /// </summary>
    private static void FlipIntoWorkArea(Popup popup)
    {
        if (popup.Child is not FrameworkElement content
            || PresentationSource.FromVisual(content) is not { } source
            || source.CompositionTarget is not { } transform)
        {
            return;
        }

        var scale = transform.TransformToDevice;
        var topLeft = content.PointToScreen(new Point(0, 0));
        var width = content.ActualWidth * scale.M11;
        var height = content.ActualHeight * scale.M22;
        var work = ScreenGeometry.WorkAreaAt(new ScreenPoint((int)topLeft.X, (int)topLeft.Y));

        if (topLeft.X + width > work.Right)
        {
            popup.HorizontalOffset -= (topLeft.X + width - work.Right) / scale.M11;
        }

        if (topLeft.Y + height > work.Bottom)
        {
            popup.VerticalOffset -= (topLeft.Y + height - work.Bottom) / scale.M22;
        }
    }

    // --- interaction ---------------------------------------------------------

    private void OnCardPressed(object sender, MouseButtonEventArgs e)
    {
        // Handled, so the press does not turn into a window drag.
        e.Handled = true;

        // The press origin arms the drag: only movement beyond the system's
        // minimum distance is a carry, everything shorter stays a click.
        _dragOrigin = e.GetPosition(null);

        if (((FrameworkElement)sender).DataContext is not BarCard card)
        {
            return;
        }

        // Ctrl-click on a link or email opens it — the same modifier whose
        // hints dress it as clickable, so the affordance and the act agree.
        if (card.IsOpenable && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OpenUri(card);
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

    /// <summary>
    /// Any card dragged with the left button carries what it is out to
    /// wherever the user drops it — text (with formatting and a plain
    /// fallback), images as pictures, files as files. Dead paths stay behind:
    /// a drag that silently produces nothing is worse than one that visibly
    /// carries less, and an all-dead card says so through a toast.
    /// </summary>

    // Where the current button press began, so a jitter during a click never
    // turns into a drag: the system's own minimum drag distance decides.
    private Point? _dragOrigin;

    private void OnCardMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragOrigin = null;
            return;
        }

        if (_dragOrigin is not { } origin)
        {
            return;
        }

        var here = e.GetPosition(null);
        if (Math.Abs(here.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(here.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        // One drag attempt per press: repeated moves must not re-enter the
        // modal loop the moment it closes.
        _dragOrigin = null;

        if (((FrameworkElement)sender).DataContext is not BarCard card)
        {
            return;
        }

        // A drag begins on movement with the button held — the user's intent
        // to carry, not to click. Everything the entry is rides along: text
        // with its formatting and a plain fallback, images as pictures,
        // files as files.
        var data = new DataObject();
        switch (card.Kind)
        {
            case EntryKind.Text:
                data.SetText(card.Text, TextDataFormat.UnicodeText);

                // Rich destinations get the RTF form; plain ones quietly use
                // the text above — one payload, both worlds. (HTML is left
                // out: WPF writes it header-less and targets mangle it.)
                if (card.Rtf is { Length: > 0 } rtf)
                {
                    data.SetText(rtf, TextDataFormat.Rtf);
                }

                break;

            case EntryKind.Image:
                if (card.Thumbnail is BitmapSource picture)
                {
                    data.SetImage(picture);
                    data.SetText(card.Text, TextDataFormat.UnicodeText);
                }
                else
                {
                    return;
                }

                break;

            default:
                var alive = card.Files.Where(File.Exists).ToList();
                if (alive.Count == 0)
                {
                    // Every path gone: a drag that silently produces nothing
                    // reads as breakage. One quiet toast says why not.
                    DeadDragNotice?.Invoke("原文件已不存在，无法拖出。");
                    return;
                }

                var dropList = new System.Collections.Specialized.StringCollection();
                dropList.AddRange([.. alive]);
                data.SetFileDropList(dropList);
                break;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
    }

    /// <summary>Sent upward so the tray can say what the card cannot.</summary>
    public event Action<string>? DeadDragNotice;

    private static void OpenUri(BarCard card)
    {
        try
        {
            var target = card.Subtype == EntrySubtype.Email
                ? "mailto:" + card.Text.Trim()
                : card.Text.Trim();

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // A link the shell cannot resolve is not worth a broken window.
        }
    }

    // --- hover actions --------------------------------------------------------

    /// <summary>Which of the user's chosen actions this card can honour, in their order.</summary>
    public IReadOnlyList<string> ActionsFor(BarCard card)
        => HoverActions.AvailableFor(
            HoverActions.Sanitise(_settings.BarActions), card.Kind, card.HasOriginal,
            DeleteIsProtected(card));

    /// <summary>
    /// Favourites and pins, under their protection switches, have no delete
    /// entry point at all. The single clear-eyed delete — un-star first —
    /// stays available, so there is no "cannot delete at all" dead end.
    /// </summary>
    private bool DeleteIsProtected(BarCard card)
        => _settings.ProtectEntries
            && ((_settings.ProtectFavorites && card.Favorite)
                || (_settings.ProtectPinned && card.IsPinned));

    private void OnCardMouseEnter(object sender, MouseEventArgs e)
    {
        // The preview hears about the card even when the tray visuals are not
        // reachable: policy first, presentation second.
        if (((FrameworkElement)sender).DataContext is BarCard entered)
        {
            RunPreviewCommand(_previewPolicy.HoverEnter(entered.Id));
        }

        if (Tree.FindDescendant<ActionTray>((DependencyObject)sender) is not { } tray
            || Tree.FindDescendant<HandoverText>((DependencyObject)sender) is not { } handover)
        {
            return;
        }

        handover.Yield();
        tray.Open();

        // Hover reveals the original behind a note: the note is the face the
        // user wrote, the content is what they come back for.
        if (((FrameworkElement)sender).DataContext is BarCard { Note.Length: > 0 } noted)
        {
            ApplyFace(noted, hovered: true);
        }
    }

    private void OnCardMouseLeave(object sender, MouseEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is BarCard)
        {
            RunPreviewCommand(_previewPolicy.HoverLeave());
        }

        if (Tree.FindDescendant<ActionTray>((DependencyObject)sender) is not { } tray
            || Tree.FindDescendant<HandoverText>((DependencyObject)sender) is not { } handover)
        {
            return;
        }

        tray.Close();
        handover.Return();

        if (((FrameworkElement)sender).DataContext is BarCard { Note.Length: > 0 } noted)
        {
            ApplyFace(noted, hovered: false);
        }
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
                _store.BumpUse(card.Id);
                if (CopyCard(card))
                {
                    Confirm(feedback, true);
                    ShowFeedback("已复制");
                }
                else
                {
                    Confirm(feedback, false);
                }
                break;

            case "plain":
                // Strips every format: plain text and nothing else, so what
                // lands carries no styling from where it came. Never offered
                // for file entries — there is no plain form to strip.
                _store.BumpUse(card.Id);
                if (_clipboard.SetText(card.Text))
                {
                    Confirm(feedback, true);
                    ShowFeedback("已按纯文本复制");
                }
                else
                {
                    Confirm(feedback, false);
                }
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

            case "favorite":
                _store.SetFavorite(card.Id, !card.Favorite);

                // In place: a favourite joins a collection and never moves,
                // so the list around it must not so much as blink.
                card.Favorite = !card.Favorite;
                break;

            case "note":
                EditNote(card);
                break;

            case "group":
                OpenGroupChooser(card, feedback);
                break;

            case "delete":
                // The keyboard path has no tray to hide; the guard answers
                // for it what the hidden button answers for the mouse.
                if (DeleteIsProtected(card))
                {
                    return;
                }

                // Snapshot before the delete: the undo re-inserts the whole
                // row — content, tags, group, note, pin — under a new id.
                var snapshot = _store.Get(card.Id);
                var groupName = snapshot is null ? null : _store.GroupOf(snapshot)?.Name;

                _store.Delete(card.Id);
                _browser.Forget(card.Id);
                RemoveCard(card);
                UpdateFooter();
                EnsureActiveItem();

                if (snapshot is not null)
                {
                    ShowFeedback($"已删除「{TruncateFeedback(snapshot)}」",
                        [(snapshot, groupName)]);
                }
                break;
        }
    }

    /// <summary>The feedback row is one line: keep a deleted name honest to it.</summary>
    private static string TruncateFeedback(Entry entry)
    {
        var text = entry.Note is { Length: > 0 } note ? note : entry.Text;
        text = text.Split('\n')[0].Trim();
        return text.Length <= 12 ? text : text[..12] + "…";
    }

    /// <summary>
    /// Copies an entry as itself: plain text always, the HTML and RTF forms
    /// when the entry has them, so a rich destination receives the formatting
    /// and a plain one receives readable text.
    /// </summary>
    private bool CopyCard(BarCard card)
        => card.Files.Count > 0
            ? _clipboard.SetFiles(card.Files)
            : card.Html is { Length: > 0 } || card.Rtf is { Length: > 0 }
                ? _clipboard.SetRich(card.Text, card.Html, card.Rtf)
                : _clipboard.SetText(card.Text);

    /// <summary>
    /// Pastes into the window the user was in before summoning the bar. The
    /// bar hides first — until it does, it is the thing in the way of the
    /// foreground the paste needs. A rich entry pastes as itself: formats
    /// written first, then the keystroke into the restored window.
    /// </summary>
    private void PasteEntry(BarCard card)
    {
        if (!_returnTo.IsSomething)
        {
            _returnTo = ForegroundWindow.Current();
        }

        // The bar is about to vanish; its preview must not be left hovering
        // over the destination the paste is about to land in.
        RunPreviewCommand(_previewPolicy.BarHidden());
        _previewTick.Stop();

        Hide();
        _returnTo.Restore();

        if (card.Files.Count > 0)
        {
            if (_clipboard.SetFiles(card.Files))
            {
                _capture.PasteCurrentClipboard();
            }
        }
        else if (card.Html is { Length: > 0 } || card.Rtf is { Length: > 0 })
        {
            if (_clipboard.SetRich(card.Text, card.Html, card.Rtf))
            {
                _capture.PasteCurrentClipboard();
            }
        }
        else
        {
            _capture.Paste(card.Text);
        }
    }

    private void OpenOriginal(BarCard card)
    {
        var target = card.Files.FirstOrDefault(File.Exists)
            ?? (card.OriginalPath is { Length: > 0 } path && File.Exists(path) ? path : null);

        if (target is null)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target)
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
        var target = card.Files.FirstOrDefault(File.Exists)
            ?? (card.OriginalPath is { Length: > 0 } path && File.Exists(path) ? path : null);

        if (target is null)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{target}\"");
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// The note editor: one box, three exits. Owned by the bar so it stays on
    /// top of it and follows it away.
    /// </summary>
    private void EditNote(BarCard card)
    {
        var editor = new Window
        {
            Title = "备注",
            Width = 340,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)FindResource("Brush.Background"),
        };

        var box = new TextBox
        {
            Text = card.Note ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 110,
            Margin = new Thickness(12),
            Padding = new Thickness(6, 4, 6, 4),
        };
        box.SetResourceReference(Control.BackgroundProperty, "Brush.SurfaceInput");
        box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 12, 12),
        };

        var save = new Button { Content = "保存", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(6, 0, 0, 0) };
        var remove = new Button { Content = "删除备注", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        var cancel = new Button { Content = "取消", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };

        save.Click += (_, _) =>
        {
            _store.SetNote(card.Id, box.Text);
            card.Note = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
            ApplyFace(card, hovered: false);
            editor.Close();
        };

        remove.Click += (_, _) =>
        {
            _store.SetNote(card.Id, null);
            card.Note = null;
            ApplyFace(card, hovered: false);
            editor.Close();
        };

        cancel.Click += (_, _) => editor.Close();

        buttons.Children.Add(cancel);
        buttons.Children.Add(remove);
        buttons.Children.Add(save);

        var panel = new StackPanel();
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        editor.Content = panel;

        box.Focus();
        box.SelectAll();
        editor.ShowDialog();
    }

    /// <summary>
    /// Puts the right text in the face: the note when there is one, the
    /// original while hovered. The note is the entry's public face because
    /// what the user wrote is what the user remembers.
    /// </summary>
    private void ApplyFace(BarCard card, bool hovered)
    {
        card.Face = card.Note is { Length: > 0 } && !hovered ? card.Note : card.Preview;
    }

    private void ReloadData()
    {
        _browser.Reset();
        RefreshTagChoices();
        Rebuild();
    }

    private void RemoveCard(BarCard card)
    {
        // A preview of a card that no longer exists is a panel describing a
        // ghost; it comes down before the row does.
        if (_preview is { IsVisible: true, CardId: var shown } && shown == card.Id)
        {
            RunPreviewCommand(_previewPolicy.Escape());
        }

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

    // --- preview panel ---------------------------------------------------------

    /// <summary>
    /// Executes one policy decision. Open and Retarget carry the panel to the
    /// card it belongs beside; Close takes it away. Everything time-based
    /// flows through the tick into here, so there is exactly one code path
    /// that shows and moves the panel.
    /// </summary>
    private void RunPreviewCommand(PreviewCommand command)
    {
        // The panel inherits this window's DPI scale: before its first show it
        // has no source of its own, and a default of 1.0 misplaces it on any
        // scaled desk.
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice
            ?? Matrix.Identity;

        switch (command)
        {
            case PreviewCommand.Open when CardById(_previewPolicy.Card) is { } opening:
                _connectorAnchor = AnchorFor(opening) ?? WindowRect();
                EnsurePreview().ShowFor(opening, _connectorAnchor.Value, slide: false,
                    scaleX: scale.M11, scaleY: scale.M22);
                break;

            case PreviewCommand.Retarget when CardById(_previewPolicy.Card) is { } moving:
                _connectorAnchor = AnchorFor(moving) ?? WindowRect();
                EnsurePreview().ShowFor(moving, _connectorAnchor.Value, slide: true,
                    scaleX: scale.M11, scaleY: scale.M22);
                break;

            case PreviewCommand.Close:
                _preview?.TakeDown();
                _connector?.HideCurve();
                break;
        }
    }

    private ConnectorWindow? _connector;

    /// <summary>The row the panel (and its connector) currently belongs beside.</summary>
    private ScreenRect? _connectorAnchor;

    private PreviewWindow EnsurePreview()
    {
        if (_preview is null)
        {
            _preview = new PreviewWindow(_fileIcons);
            _preview.PointerRestingOnPanel += () => RunPreviewCommand(_previewPolicy.PreviewEntered());
            _preview.PointerLeftPanel += () => RunPreviewCommand(_previewPolicy.PreviewLeft());
            _preview.PanelMoved += OnPreviewPanelMoved;
        }

        return _preview;
    }

    /// <summary>
    /// The connector redraws with every step the panel takes — first arrival
    /// included, which is the teleport the ticket asks for: a line flying in
    /// from the previous row's position would read as a glitch, not as craft.
    /// </summary>
    private void OnPreviewPanelMoved(ScreenRect panel)
    {
        if (_connectorAnchor is not { } anchor)
        {
            return;
        }

        _connector ??= new ConnectorWindow();
        _connector.ShowCurve(anchor, panel, new System.Windows.Interop.WindowInteropHelper(_preview).Handle);
    }

    /// <summary>The panel follows the pointer only between realised cards; off-list the bar anchors it.</summary>
    private BarCard? CardById(long? id)
        => id is { } key ? VisibleRows.FirstOrDefault(card => card.Id == key) : null;

    /// <summary>
    /// The hovered or selected card's rectangle in physical pixels — where the
    /// preview hangs from. Null when the card is not realized (a keyboard move
    /// still scrolling into view); the settle beat usually resolves that.
    ///
    /// Built from the window's own physical rectangle plus the card's offset
    /// inside it, rather than PointToScreen: on a desk with mixed scale
    /// factors, PointToScreen composes through the wrong monitor's transform
    /// and the anchor lands on the wrong screen.
    /// </summary>
    private ScreenRect? AnchorFor(BarCard card)
    {
        var container = PinnedList.ItemContainerGenerator.ContainerFromItem(card);
        if (container is null)
        {
            container = Cards.ItemContainerGenerator.ContainerFromItem(card);
        }

        if (container is not FrameworkElement element || element.ActualWidth <= 0)
        {
            return null;
        }

        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (!WindowRects.TryGet(handle, out var window))
        {
            return null;
        }

        var offset = element.TranslatePoint(new Point(0, 0), this);
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice
            ?? Matrix.Identity;

        var anchorRect = new ScreenRect(
            window.Left + (int)Math.Round(offset.X * scale.M11),
            window.Top + (int)Math.Round(offset.Y * scale.M22),
            window.Left + (int)Math.Round((offset.X + element.ActualWidth) * scale.M11),
            window.Top + (int)Math.Round((offset.Y + element.ActualHeight) * scale.M22));

        return anchorRect;
    }

    private ScreenRect WindowRect()
        => WindowRects.TryGet(new System.Windows.Interop.WindowInteropHelper(this).Handle, out var rect)
            ? rect
            : new ScreenRect(0, 0, 0, 0);

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

        // Noted, not followed: the panel waits for the selection to settle so
        // it glides to a still target instead of chasing a scrolling one.
        RunPreviewCommand(_previewPolicy.SelectionMoved(rows[next].Id));
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

            // Links and emails dress as clickable for exactly as long as the
            // modifier that opens them is held. On release the resource
            // reference is restored rather than a local colour set, so theme
            // changes keep reaching these texts.
            if (Tree.FindDescendant<EntryBodyText>(container) is { } body)
            {
                var clickable = on
                    && ((FrameworkElement)container).DataContext is BarCard { IsOpenable: true };

                if (clickable)
                {
                    body.Foreground = (Brush)FindResource("Brush.Accent");
                    body.TextDecorations = System.Windows.TextDecorations.Underline;
                }
                else
                {
                    body.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                    body.TextDecorations = null;
                }
            }
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
        // Releasing Space takes down only what Space opened — a hover preview
        // under the pointer keeps its own rules. The guard is loose on
        // purpose: focus may have wandered between press and release.
        if (e.Key is Key.Space)
        {
            RunPreviewCommand(_previewPolicy.SpaceUp());
        }

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

                // The menu peels off first: closing it must not cost the user
                // their place — the bar stays, the selection stays.
                if (_cardMenu is { IsOpen: true } || _cardSubMenu is { IsOpen: true })
                {
                    CloseCardMenu();
                    return;
                }

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

            // Held Space previews the active card in full (ticket 17). The
            // search box is focused right after summoning, so the rule has to
            // tell an empty box from a query being typed: with nothing typed,
            // Space is free to mean "show me"; mid-query it stays a space.
            case Key.Space when Keyboard.Modifiers == ModifierKeys.None
                && (SearchBox.Text.Length == 0 || !IsTyping):
                e.Handled = true;
                if (_selected is { } card)
                {
                    RunPreviewCommand(_previewPolicy.SpaceDown(card.Id));
                }
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

            case Key.S when !IsTyping && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                if (_selected is { } favourite) ExecuteAction("favorite", favourite, feedback: null);
                break;

            case Key.N when !IsTyping && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                if (_selected is { } note) ExecuteAction("note", note, feedback: null);
                break;

            case Key.G when !IsTyping && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                if (_selected is { } group) ExecuteAction("group", group, feedback: null);
                break;

            case Key.D when !IsTyping && Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = true;
                if (_selected is { } del) ExecuteAction("delete", del, feedback: null);
                break;

            // The undo window is narrow on purpose: it answers only while the
            // footer is still showing the deletion it would reverse.
            case Key.Z when !IsTyping && Keyboard.Modifiers == ModifierKeys.None && _undoPending is not null:
                e.Handled = true;
                RestoreUndo();
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

        switch (BarKeyboard.NextEscape(
            previewOpen: _preview is { IsVisible: true },
            hasTag,
            hasKind))
        {
            case BarKeyboard.EscapeAction.ClosePreview:
                // The panel's level in the stack, ready since the keyboard
                // model was written; this is its wiring.
                RunPreviewCommand(_previewPolicy.Escape());
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
