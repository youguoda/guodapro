using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>
/// The badge: a small, brief offer to translate what was just copied.
///
/// It must never take focus. The user is in the middle of something in another
/// application — their selection, their caret, their keyboard — and a window
/// that steals any of that is worse than no badge at all.
/// </summary>
public partial class BadgeWindow : Window
{
    // The hold is what the spec calls "one to two seconds"; the fade-out goes
    // through Motion, the app-wide motion system (ticket 05). The fade-in is
    // deliberately absent: WPF skips compositing a layered window whose
    // current surface is fully transparent, so an opacity animation starting
    // at 0 never gets its first tick and the badge stays invisible forever —
    // found during human acceptance of ticket 04; the same frozen-clock
    // behaviour as the ListSpike finding.
    private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(2000);

    private readonly DispatcherTimer _dismiss;
    private string _text = string.Empty;

    /// <summary>Raised when the user takes up the offer.</summary>
    public event Action<string>? Accepted;

    public BadgeWindow()
    {
        InitializeComponent();

        _dismiss = new DispatcherTimer { Interval = Hold };
        _dismiss.Tick += (_, _) =>
        {
            _dismiss.Stop();
            FadeAway();
        };

        // Hovering is the user saying "wait, I am reading this" — so stop the
        // clock rather than pulling it away mid-decision.
        MouseEnter += (_, _) => _dismiss.Stop();
        MouseLeave += (_, _) => _dismiss.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // WPF's ShowActivated=false covers the initial show. WS_EX_NOACTIVATE
        // covers everything after it — clicking the badge must not pull focus
        // away from whatever the user is typing in.
        TransientWindow.MakeNonActivating(new WindowInteropHelper(this).Handle, ZBand.Topmost);
    }

    /// <summary>Shows the badge beside the cursor for the given text.</summary>
    public void Offer(string text)
    {
        _text = text;

        _dismiss.Stop();
        BeginAnimation(OpacityProperty, null);

        // Full opacity immediately — a fade-in starting from 0 never renders
        // (see field notes above). The fade-out below is the one that works,
        // because it starts from a painted surface.
        Opacity = 1;

        // Shown before measuring: the size is not known until it has been laid
        // out, and the placement arithmetic needs it.
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        MoveBesideCursor();

        _dismiss.Start();
    }

    private void MoveBesideCursor()
    {
        var cursor = ScreenGeometry.CursorPosition();
        var workArea = ScreenGeometry.WorkAreaAt(cursor);

        var handle = new WindowInteropHelper(this).Handle;
        var source = PresentationSource.FromVisual(this);

        // ActualWidth is in device-independent units; the placement and
        // SetWindowPos both speak physical pixels, and on a scaled monitor
        // those are not the same number.
        var scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scaleY);

        var placed = BadgePlacement.Place(cursor, width, height, workArea);

        // A global surface: the badge is summoned by copies anywhere and
        // relies on the topmost band to be seen at all.
        TransientWindow.MoveTo(handle, placed, ZBand.Topmost);
    }

    private void OnClicked(object sender, MouseButtonEventArgs e)
    {
        _dismiss.Stop();
        BeginAnimation(OpacityProperty, null);
        Hide();
        Accepted?.Invoke(_text);
    }

    private void FadeAway()
    {
        var fade = Motion.Fade(0);
        fade.Completed += (_, _) =>
        {
            // Hidden rather than closed: the badge appears many times an hour,
            // and building a window each time is work the user would feel.
            if (Opacity <= 0.01)
            {
                Hide();
            }
        };

        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Lets the application close it for real on shutdown.</summary>
    public void CloseForGood()
    {
        _dismiss.Stop();
        Close();
    }
}
