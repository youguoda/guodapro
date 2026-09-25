using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// The hover tray on a card: its quick actions squeeze in from the edge —
/// width 0→20 with a slight scale and slide, on the motion system's fast
/// tier — and squeeze back out on leave. Squeezing rather than popping is
/// what keeps the row from jerking.
///
/// Built per card, because the action set differs per entry kind. All motion
/// lives in code rather than style storyboards on purpose: with container
/// recycling the same tray instance moves to another row's card, and only
/// code can tear the previous row's animation state down at that moment
/// (<see cref="Reset"/> — the issue 02 spike's finding).
/// </summary>
internal sealed class ActionTray : StackPanel
{
    private const int ButtonSize = 20;

    public ActionTray()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>Raises the executed action. Set by the window.</summary>
    public event Action<string, BarCard, Button>? ActionExecuted;

    /// <summary>Rebuilds the buttons for the card this tray now belongs to.</summary>
    public void Configure(BarCard card, IReadOnlyList<string> actions)
    {
        Children.Clear();

        foreach (var id in actions)
        {
            Children.Add(MakeButton(id, card));
        }
    }

    private Button MakeButton(string id, BarCard card)
    {
        var button = new Button
        {
            Width = 0,
            Height = ButtonSize,
            ClipToBounds = true,
            Focusable = false,
            Tag = id,
            Content = HoverActions.Glyph(id),
            Cursor = Cursors.Hand,
            ToolTip = HoverActions.Name(id),
            RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(0.9, 0.9), new TranslateTransform(4, 0) },
            },
            RenderTransformOrigin = new Point(0.5, 0.5),
            Style = (Style)TryFindResource("TrayButtonStyle") ?? new Style(typeof(Button)),
        };

        // Theme-following through resources; destructive actions keep their
        // own colour so they never read as ordinary.
        button.SetResourceReference(Control.ForegroundProperty,
            HoverActions.IsDestructive(id) ? "Brush.Danger" : "Brush.Text");

        // Every mouse event stops here — on the bubbling versions, after the
        // button's own class handlers have run, so the button still clicks
        // while the press never reaches the card underneath. Swallowing the
        // preview events instead kills the button's Click mechanism outright,
        // which is how the tray first shipped: visible, Pressable, dead.
        button.MouseLeftButtonDown += Swallow;
        button.MouseLeftButtonUp += Swallow;
        button.MouseRightButtonDown += Swallow;
        button.MouseRightButtonUp += Swallow;
        button.MouseDown += Swallow;
        button.MouseUp += Swallow;

        button.Click += (_, _) => ActionExecuted?.Invoke(id, card, button);

        return button;

        static void Swallow(object sender, MouseButtonEventArgs e) => e.Handled = true;
    }

    /// <summary>
    /// While Ctrl is held, buttons that answer to a letter show the letter
    /// instead of their glyph — the badge and the key handler both read
    /// <see cref="BarKeys"/>, so they cannot disagree. Buttons without a key
    /// keep their glyph.
    /// </summary>
    public void ShowHints(bool on)
    {
        foreach (var button in Children.OfType<Button>())
        {
            if (button.Tag is not string id)
            {
                continue;
            }

            var key = BarKeys.TrayKey(id);
            button.Content = on && key is not null ? key : HoverActions.Glyph(id);
        }
    }

    public void Open() => Animate(open: true);

    public void Close() => Animate(open: false);

    /// <summary>
    /// Called when the owning container is recycled onto another row: any
    /// in-flight or held animation from the previous row is removed and the
    /// tray snaps to its closed base state, so the new row starts clean.
    /// </summary>
    public void Reset()
    {
        foreach (var button in Children.OfType<Button>())
        {
            button.BeginAnimation(WidthProperty, null);

            var group = (TransformGroup)button.RenderTransform;
            var scale = (ScaleTransform)group.Children[0];
            var translate = (TranslateTransform)group.Children[1];

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            translate.BeginAnimation(TranslateTransform.XProperty, null);
        }
    }

    private void Animate(bool open)
    {
        foreach (var button in Children.OfType<Button>())
        {
            // To-only: an interrupted squeeze continues from wherever it is,
            // so re-hovering mid-close reads as one smooth reversal.
            button.BeginAnimation(WidthProperty, Motion.Double(open ? ButtonSize : 0));

            var group = (TransformGroup)button.RenderTransform;
            var scale = (ScaleTransform)group.Children[0];
            var translate = (TranslateTransform)group.Children[1];

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.Double(open ? 1 : 0.9));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.Double(open ? 1 : 0.9));
            translate.BeginAnimation(TranslateTransform.XProperty, Motion.Double(open ? 0 : 4));
        }
    }
}
