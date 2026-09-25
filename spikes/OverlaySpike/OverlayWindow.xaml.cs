using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace OverlaySpike;

/// <summary>
/// The overlay under test: full screen, transparent, topmost, never activated,
/// carrying a connector curve and one clickable panel.
/// </summary>
public partial class OverlayWindow : Window
{
    private Point _source;

    public OverlayWindow()
    {
        InitializeComponent();
    }

    /// <summary>Set by the host so the spike can report what was clicked.</summary>
    public Action<string>? Reported { get; set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // ShowActivated=false covers the first show; WS_EX_NOACTIVATE covers
        // every click afterwards. Without the second, clicking the panel would
        // pull focus out of whatever the user was typing in.
        Native.MakeNonActivating(new WindowInteropHelper(this).Handle);
    }

    /// <summary>
    /// Covers one screen, sized through WPF's own properties.
    ///
    /// Deliberately one screen rather than the whole virtual desktop. Two
    /// earlier attempts here were instructive: sizing in device-independent
    /// units across monitors of different scale factors produces a window whose
    /// internal coordinates do not match the screen, and sizing it with
    /// SetWindowPos in physical pixels is worse — WPF's transforms go on
    /// believing the window is where WPF put it, so PointToScreen and Windows'
    /// own hit-testing disagree about where anything is.
    ///
    /// Letting WPF own the geometry keeps one coordinate system, and a preview
    /// overlay never needs to span monitors anyway: it belongs to the screen
    /// its source row is on.
    /// </summary>
    public void CoverScreen()
    {
        var area = SystemParameters.WorkArea;

        Left = area.Left;
        Top = area.Top;
        Width = area.Width;
        Height = area.Height;
    }

    /// <summary>
    /// Points the connector at a rectangle given in **physical screen pixels**,
    /// and parks the panel to its right.
    /// </summary>
    public void ConnectTo(Rect sourceOnScreen)
    {
        // PointFromScreen does the physical-to-local conversion using this
        // window's own transform, which is the only conversion that stays
        // correct when monitors have different scale factors.
        var source = PointFromScreen(new Point(
            sourceOnScreen.Right,
            sourceOnScreen.Top + (sourceOnScreen.Height / 2)));

        _source = source;

        var panelLeft = source.X + 120;
        var panelTop = source.Y - (Panel.Height / 2);

        Canvas.SetLeft(Panel, panelLeft);
        Canvas.SetTop(Panel, panelTop);

        var target = new Point(panelLeft, panelTop + (Panel.Height / 2));

        DrawConnector(source, target);
        HasConnected = true;
    }

    private void DrawConnector(Point from, Point to)
    {
        // Control points projected horizontally by a fraction of the span, so
        // the curve leaves and arrives perpendicular to the two edges.
        var reach = Math.Clamp(Math.Abs(to.X - from.X) * 0.45, 20, 90);

        var figure = new PathFigure { StartPoint = from, IsClosed = false };
        figure.Segments.Add(new BezierSegment(
            new Point(from.X + reach, from.Y),
            new Point(to.X - reach, to.Y),
            to,
            isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        Connector.Data = geometry;

        Place(SourceDot, from);
        Place(PanelDot, to);
    }

    private static void Place(FrameworkElement dot, Point at)
    {
        Canvas.SetLeft(dot, at.X - (dot.Width / 2));
        Canvas.SetTop(dot, at.Y - (dot.Height / 2));
    }

    private void OnPanelButtonClick(object sender, RoutedEventArgs e)
    {
        PanelStatus.Text = $"面板收到了点击 · {DateTime.Now:HH:mm:ss}";
        Reported?.Invoke("panel button clicked");
    }

    /// <summary>
    /// The panel's centre in physical screen pixels, asked of WPF rather than
    /// derived by hand — with monitors at different scale factors, doing the
    /// arithmetic ourselves is exactly where it goes wrong.
    /// </summary>
    public Point PanelCentreOnScreen
        => Panel.PointToScreen(new Point(Panel.Width / 2, Panel.Height / 2));

    /// <summary>The panel's top-left corner in physical screen pixels.</summary>
    public Point PanelTopLeftOnScreen
        => Panel.PointToScreen(new Point(0, 0));

    /// <summary>
    /// What WPF itself thinks is under a point inside the overlay. If WPF finds
    /// the panel but Windows does not, the problem is OS-level hit-testing
    /// rather than our layout.
    /// </summary>
    public string HitTestInsideOverlay(Point overlayLocal)
    {
        var result = VisualTreeHelper.HitTest(Surface, overlayLocal);
        return result?.VisualHit switch
        {
            null => "(nothing)",
            var hit => hit.GetType().Name + NameOf(hit),
        };

        static string NameOf(DependencyObject hit)
            => hit is FrameworkElement { Name.Length: > 0 } element ? $" '{element.Name}'" : string.Empty;
    }

    /// <summary>The panel's centre in overlay-local units, for the WPF hit test.</summary>
    public Point PanelCentreLocal => new(
        Canvas.GetLeft(Panel) + (Panel.Width / 2),
        Canvas.GetTop(Panel) + (Panel.Height / 2));

    /// <summary>Whether <see cref="ConnectTo"/> has positioned anything yet.</summary>
    public bool HasConnected { get; private set; }

    /// <summary>Raw state, so the self-test can tell a real answer from a layout bug.</summary>
    public string DescribeLayout()
        => $"Canvas.Left={Canvas.GetLeft(Panel):0.##} Canvas.Top={Canvas.GetTop(Panel):0.##} "
           + $"window=({Left:0.##},{Top:0.##}) size=({Width:0.##}x{Height:0.##}) "
           + $"panelVisible={Panel.IsVisible} connected={HasConnected} "
           + $"connectorFigures={(Connector.Data as PathGeometry)?.Figures.Count ?? 0}";
}
