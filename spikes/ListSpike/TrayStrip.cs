using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ListSpike;

/// <summary>
/// The hover tray: twelve 20px buttons that squeeze in from the card's right
/// edge (width 0→20, scale 0.9→1, translate +4→0, ~160ms), and squeeze back
/// out on leave.
///
/// The motion lives in code on purpose. With VirtualizationMode=Recycling the
/// same container — and therefore this same tray instance — is handed to
/// another row, and whatever animation state the previous row left behind must
/// be torn down at that moment. A style storyboard cannot do that;
/// <see cref="Reset"/>, called from the container's recycling hooks, can.
/// </summary>
internal sealed class TrayStrip : StackPanel
{
    private const int ButtonSize = 20;
    private const int ButtonCount = 12;

    private static readonly string[] Actions =
        { "复", "藏", "删", "编", "译", "总", "并", "改", "签", "开", "搜", "钉" };

    private static readonly Duration Slide = new(TimeSpan.FromMilliseconds(160));
    private static readonly IEasingFunction SqueezeIn = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction SqueezeOut = new CubicEase { EasingMode = EasingMode.EaseIn };

    public TrayStrip()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;

        for (var i = 0; i < ButtonCount; i++)
        {
            Children.Add(MakeButton(Actions[i]));
        }

        // Resolved late rather than in MakeButton: window-scoped resources are
        // reachable from the element tree once this strip is in it, and asking
        // earlier would race whatever loads them.
        Loaded += (_, _) =>
        {
            foreach (var button in Children.OfType<Button>())
            {
                button.Style = (Style)FindResource("TrayButton");
            }
        };
    }

    private static Button MakeButton(string action)
    {
        var button = new Button
        {
            Width = 0,
            Height = ButtonSize,
            ClipToBounds = true,
            Focusable = false,
            Tag = action,
            Content = action,
            RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(0.9, 0.9), new TranslateTransform(4, 0) },
            },
            RenderTransformOrigin = new Point(0.5, 0.5),
        };

        return button;
    }

    public void Open() => Animate(open: true);

    public void Close() => Animate(open: false);

    /// <summary>
    /// Called when the owning container is recycled onto another row: any
    /// in-flight or held animation from the previous row is removed and the
    /// tray snaps back to its closed base state, so the new row starts clean.
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

    /// <summary>Current effective widths, animated value included — for the self-test.</summary>
    public IReadOnlyList<double> ButtonWidths()
        => Children.OfType<Button>().Select(b => b.Width).ToList();

    private void Animate(bool open)
    {
        foreach (var button in Children.OfType<Button>())
        {
            // To-only, so an animation interrupted mid-flight continues from
            // wherever it currently is: re-hovering while the tray is still
            // closing feels continuous rather than snapping.
            button.BeginAnimation(WidthProperty, new DoubleAnimation
            {
                To = open ? ButtonSize : 0,
                Duration = Slide,
                EasingFunction = open ? SqueezeIn : SqueezeOut,
            });

            var group = (TransformGroup)button.RenderTransform;
            var scale = (ScaleTransform)group.Children[0];
            var translate = (TranslateTransform)group.Children[1];

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation
            {
                To = open ? 1 : 0.9,
                Duration = Slide,
                EasingFunction = open ? SqueezeIn : SqueezeOut,
            });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation
            {
                To = open ? 1 : 0.9,
                Duration = Slide,
                EasingFunction = open ? SqueezeIn : SqueezeOut,
            });
            translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
            {
                To = open ? 0 : 4,
                Duration = Slide,
                EasingFunction = open ? SqueezeIn : SqueezeOut,
            });
        }
    }
}
