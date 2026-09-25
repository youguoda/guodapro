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
        AppSettings settings)
    {
        InitializeComponent();

        _hotkeys = hotkeys;
        _clipboard = clipboard;
        _backend = backend;
        _target = settings.TargetLanguage;
        _source = settings.SourceLanguage;
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

        // Leaving fades out through the app-wide motion system: from a
        // painted surface, which composites fine, and instant when Windows
        // asks for reduced motion.
        var fade = Motion.Fade(0);
        fade.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, fade);
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
