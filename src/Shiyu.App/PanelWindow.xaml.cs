using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>
/// The translation panel: the original above, the translation growing beneath
/// it as the words arrive.
///
/// Like the badge, it never takes focus. That leaves it unable to receive key
/// presses, so Escape is a global hotkey held only while the panel is on
/// screen — see <see cref="_escape"/>.
/// </summary>
public partial class PanelWindow : Window
{
    private readonly HotkeyRegistry _hotkeys;
    private readonly WindowsClipboardWriter _clipboard;
    private readonly Func<ITranslationBackend> _backend;
    private readonly Action<string, string>? _saveTranslation;

    private IDisposable? _escape;
    private CancellationTokenSource? _inFlight;
    private TranslationSession? _session;
    private string _original = string.Empty;
    private string _target;
    private string? _source;

    public PanelWindow(
        HotkeyRegistry hotkeys,
        WindowsClipboardWriter clipboard,
        Func<ITranslationBackend> backend,
        AppSettings settings,
        Action<string, string>? saveTranslation = null)
    {
        InitializeComponent();

        _hotkeys = hotkeys;
        _clipboard = clipboard;
        _backend = backend;
        _saveTranslation = saveTranslation;
        _target = settings.TargetLanguage;
        _source = settings.SourceLanguage;

        Backdrop.Attach(this, () => BackdropKind.Acrylic);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TransientWindow.MakeNonActivating(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Shows the panel beside the cursor and starts translating.</summary>
    public async Task TranslateAsync(string text)
    {
        _original = text;
        OriginalText.Text = text;
        TranslatedText.Text = string.Empty;
        StatusText.Visibility = Visibility.Collapsed;
        SaveButton.Content = "存入历史";
        UpdateDirectionLabel();

        if (!IsVisible)
        {
            Show();
        }

        // Full opacity immediately, no fade-in: a layered window fading in
        // from transparency never composites (ticket 04's badge finding), so
        // the panel would rely on the translation stream's layout churn to
        // rescue it — arriving late or, with a fast backend, not at all.
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        UpdateLayout();
        MoveBesideCursor();

        // The entrance lives on the content, not the window: same rule,
        // opposite side — the sheet fades and rises, the window stays solid.

        HoldEscape();
        await RunTranslation();
    }

    private async Task RunTranslation()
    {
        // A second request while the first is still arriving abandons it; the
        // user has moved on and the old stream's text would interleave.
        _inFlight?.Cancel();
        _inFlight?.Dispose();
        _inFlight = new CancellationTokenSource();

        var session = new TranslationSession(_backend());
        _session = session;

        session.Updated += () => Dispatcher.Invoke(() =>
        {
            if (!ReferenceEquals(_session, session))
            {
                return;
            }

            TranslatedText.Text = session.Text;

            // A stream reads like a conversation: follow the newest line
            // unless the user scrolled up to re-read.
            if (TranslationScroll.ScrollableHeight > 0
                && TranslationScroll.VerticalOffset >= TranslationScroll.ScrollableHeight - 24)
            {
                TranslationScroll.ScrollToEnd();
            }

            if (session.State == TranslationState.Failed && session.Error is { } error)
            {
                // The partial text stays on screen beneath the error: two
                // thirds of a translation is still two thirds of what was
                // wanted, and taking it away would be a second failure.
                StatusText.Text = error;
                StatusText.Visibility = Visibility.Visible;
            }

            UpdateLayout();
        });

        await session.RunAsync(
            new TranslationRequest(_original, _target) { SourceLanguage = _source },
            _inFlight.Token);

        // Kept work needs a door: only a finished translation is worth saving,
        // and the button says what it will do with it.
        SaveButton.IsEnabled = _saveTranslation is not null
            && session.State == TranslationState.Finished
            && session.Text.Trim().Length > 0;
    }

    /// <summary>
    /// Hands the finished pair to whoever owns history. The link to the
    /// original entry is resolved there — here there is only text.
    /// </summary>
    private void OnSaveToHistory(object sender, RoutedEventArgs e)
    {
        if (_saveTranslation is null || _session?.Text.Trim() is not { Length: > 0 } translated)
        {
            return;
        }

        _saveTranslation(_original, translated);
        SaveButton.IsEnabled = false;
        SaveButton.Content = "✓ 已存入";
    }

    private void MoveBesideCursor()
    {
        var cursor = ScreenGeometry.CursorPosition();
        var workArea = ScreenGeometry.WorkAreaAt(cursor);

        var source = PresentationSource.FromVisual(this);
        var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        var placed = BadgePlacement.Place(
            cursor,
            (int)Math.Ceiling(ActualWidth * scaleX),
            (int)Math.Ceiling(ActualHeight * scaleY),
            workArea);

        TransientWindow.MoveTo(new WindowInteropHelper(this).Handle, placed);
    }

    /// <summary>
    /// Takes Escape for as long as the panel is up. Releasing it reliably
    /// matters more than taking it: an Escape left registered would be
    /// swallowed system-wide.
    /// </summary>
    private void HoldEscape()
    {
        _escape ??= _hotkeys.TryRegisterScoped(
            new Hotkey(HotkeyModifiers.None, 0x1B, "关闭面板"),
            () => Dispatcher.Invoke(Dismiss));

        // Escape being unavailable costs the user a click on the close button;
        // it is not worth refusing to show a translation over.
        HintText.Text = _escape is null ? "点 ✕ 关闭" : "Esc 关闭";
    }

    private void ReleaseEscape()
    {
        _escape?.Dispose();
        _escape = null;
    }

    private void Dismiss()
    {
        _inFlight?.Cancel();
        ReleaseEscape();

        // Instant hide. A fade here reads as jank on a layered window — the
        // text drops out before the tinted sheet does (the user's own words:
        // "先没有字体，再一个灰板") — and the system's Win+V panel, the
        // benchmark for this exact surface, also pops shut with no exit.
        Hide();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Dismiss();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_session is null || _session.Text.Length == 0)
        {
            return;
        }

        CopyButton.Content = _clipboard.SetText(_session.Text) ? "已复制" : "复制失败";
    }

    private async void OnSwapDirection(object sender, RoutedEventArgs e)
    {
        // Swapping only makes sense between two named languages; with the
        // source left to the backend there is nothing to swap it with.
        (_source, _target) = (_target, _source ?? "English");
        UpdateDirectionLabel();
        CopyButton.Content = "复制译文";
        await RunTranslation();
    }

    private void UpdateDirectionLabel()
        => DirectionLabel.Text = $"{_source ?? "自动识别"} → {_target}";

    /// <summary>Lets the application close it for real on shutdown.</summary>
    public void CloseForGood()
    {
        _inFlight?.Cancel();
        ReleaseEscape();
        Close();
    }
}
