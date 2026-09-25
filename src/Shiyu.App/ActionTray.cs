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

        // Every mouse event stops here. The card underneath treats a press as
        // selection and a double-press as a copy; a tray press is none of the
        // card's business. Middle and right buttons included — six events is
        // the reference implementation's hard-won list, not padding.
        button.PreviewMouseLeftButtonDown += Swallow;
        button.PreviewMouseLeftButtonUp += Swallow;
        button.PreviewMouseRightButtonDown += Swallow;
        button.PreviewMouseRightButtonUp += Swallow;
        button.PreviewMouseDown += Swallow;
        button.PreviewMouseUp += Swallow;

        button.Click += (_, _) => ActionExecuted?.Invoke(id, card, button);

        return button;

        static void Swallow(object sender, MouseButtonEventArgs e) => e.Handled = true;
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
