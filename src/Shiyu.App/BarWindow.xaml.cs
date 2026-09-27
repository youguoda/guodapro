using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

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
    private ForegroundWindow _returnTo;

    /// <summary>Raised when the window is moved or resized; the owner persists geometry, throttled its own way.</summary>
    public event Action? GeometryChanged;

    /// <summary>
    /// Raised when the bar changes a setting itself (the header's pin, 票 39);
    /// the owner writes it down. The settings page flows the other way, through
    /// <see cref="ApplySettings"/> — one value, two editors, no loop.
    /// </summary>
    public event Action<AppSettings>? SettingsChanged;

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

        // Kind is a segmented control now: four chips, one index.
        _kindChips = [KindChipAll, KindChipText, KindChipImage, KindChipFiles];
        SetKindIndex(0);

        PinnedList.ItemsSource = _pinned;
        Cards.ItemsSource = _cards;

        // The pin state is a setting, not a mood (票 39): whatever the file
        // says is how the window opens, and the header chrome agrees with it.
        Topmost = _settings.BarAlwaysOnTop;
        SyncTopmostChrome();

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
        ApplyTopmost(settings.BarAlwaysOnTop);
        Rebuild();
    }

    // --- the pin (票 39) ---------------------------------------------------------

    /// <summary>
    /// The header pin. Topmost is the resident bar's working posture; turning
    /// it off is a deliberate act, written to the settings the moment it
    /// happens so the posture survives the restart. It is a mode, not a layer:
    /// it never joins the Esc stack.
    /// </summary>
    private void OnTopmostToggle(object sender, RoutedEventArgs e)
    {
        var wanted = !Topmost;
        _settings = _settings with { BarAlwaysOnTop = wanted };
        ApplyTopmost(wanted);
        SettingsChanged?.Invoke(_settings);
    }

    private void ApplyTopmost(bool topmost)
    {
        if (Topmost == topmost)
        {
            return;
        }

        Topmost = topmost;
        SyncTopmostChrome();

        // Degradation policy (票 39 评审定案): the layers ANCHORED to this
        // window — the preview panel and its connector — follow the bar's
        // z-tier, so a covered bar is never shadowed by its own floating
        // panes. The badge and the translation panel are independent
        // surfaces summoned by copies anywhere, not bar layers; they keep
        // their own Topmost.
        SyncFloatingLayers();
    }

    /// <summary>The pin button's face: filled and accented while pinned, quiet otherwise.</summary>
    private void SyncTopmostChrome()
    {
        if (Topmost)
        {
            // PinnedFill. The ticket's "E8417" is a five-digit slip — the
            // icon fonts stop at U+F8CC and nothing beyond the BMP exists;
            // E841 is the filled pin both Segoe icon fonts carry.
            TopmostGlyph.Text = "\uE841";
            TopmostGlyph.SetResourceReference(ForegroundProperty, "Brush.Accent");
            TopmostToggle.ToolTip = "窄条置顶中（点击后可被其他窗口遮挡）";
        }
        else
        {
            TopmostGlyph.Text = "\uE718";
            TopmostGlyph.SetResourceReference(ForegroundProperty, "Brush.TextSecondary");
            TopmostToggle.ToolTip = "窄条未置顶（点击恢复保持在其他窗口之上）";
        }
    }

    private void SyncFloatingLayers()
    {
        if (_preview is not null)
        {
            _preview.Topmost = Topmost;
        }

        if (_connector is not null)
        {
            _connector.Topmost = Topmost;
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

        // First-use teaching: the interactions are good but invisible — the
        // footer mentions them for the first few summons, then never again.
        if (FirstUseHints.ShowOnSummon)
        {
            FirstUseHints.RegisterSummon();
            ShowFirstUseHint();
        }
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

        // The band rides with every placement (验收缺陷 A): the raw move once
        // hard-inserted HWND_TOPMOST, so a bar whose setting said "not
        // topmost" was resurrected above everything on every summon. The
        // window's own Topmost — kept in step with the setting — decides the
        // band here, and the XAML no longer seeds the bit at parse time.
        TransientWindow.MoveTo(helper.Handle, placed, ZBandPolicy.FollowsHost(Topmost));
    }

    /// <summary>Hides with the standard fade, from a painted surface, and gives focus back.</summary>
    public void Dismiss()
    {
        _behindCheck.Stop();
        _previewTick.Stop();
        RunPreviewCommand(_previewPolicy.BarHidden());

        // Instant hide, matching the instant summon. Fading a layered window
        // out reads as "text first, then a grey sheet" — the user's words —
        // and the system's Win+V panel, the benchmark surface, closes with no
        // exit either. For a 3-second tool, speed *is* the polish.
        DismissFirstUseHint();
        Hide();
        _returnTo.Restore();
        EnterLightweightIfEnabled();
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

        // Strand check against the WHOLE virtual desktop, not the primary
        // work area: the bar legitimately lives on either monitor of this
        // dual-screen desk, and a primary-only check yanked it back every
        // restart — the cross-screen disconnect the user reported. Two
        // monitors at different scales make the DIU→physical mapping loose,
        // so the containment test carries generous slack by design.
        var vx = SystemParameters.VirtualScreenLeft;
        var vy = SystemParameters.VirtualScreenTop;
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        var onScreen = Left + Width > vx && Left < vx + vw && Top + Height > vy && Top < vy + vh;

        if (!onScreen)
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
}
