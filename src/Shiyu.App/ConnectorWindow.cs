using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>
/// The connector layer (ticket 18): one thin bezier and two endpoint dots,
/// tying the preview panel to the row it previews.
///
/// The window is a click-through sheet over the small region the curve
/// occupies — it intercepts nothing, so the panel beside it stays the only
/// thing that reacts. It sits directly below the preview in the z-order, so
/// the curve visually disappears under the panel's edge rather than over it.
/// </summary>
internal partial class ConnectorWindow : Window
{
    private readonly Canvas _sheet = new();
    private readonly System.Windows.Shapes.Path _stroke = new()
    {
        StrokeThickness = 1.5,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    private readonly System.Windows.Shapes.Ellipse _fromDot = NewDot();

    private readonly System.Windows.Shapes.Ellipse _toDot = NewDot();

    public ConnectorWindow()
    {
        // Built entirely in code — the sheet is two shapes, and a XAML file
        // would be ceremony around them.
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;

        _sheet.IsHitTestVisible = false;
        _stroke.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Brush.Accent");
        _stroke.Opacity = 0.55;
        _sheet.Children.Add(_stroke);
        _sheet.Children.Add(_fromDot);
        _sheet.Children.Add(_toDot);

        Content = _sheet;
    }

    private static System.Windows.Shapes.Ellipse NewDot()
    {
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 5,
            Height = 5,
            IsHitTestVisible = false,
        };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.Accent");
        return dot;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Clicks pass through the whole sheet — the curve is explanation, not
        // a surface. NOACTIVATE for the same reason the panel carries it.
        // The interop lives in the platform layer (O-39).
        TransientWindow.MakeClickThrough(new WindowInteropHelper(this).Handle);
    }

    /// <summary>
    /// Positions the sheet over the region the curve spans and redraws it.
    /// The card and panel rectangles arrive in physical pixels; the sheet
    /// converts to DIPs through the scale of the MONITOR THAT REGION SITS ON
    /// (U-03) — never its own <c>PresentationSource</c>, which still answers
    /// for the previous monitor for a beat after every cross-DPI move and
    /// lands the curve inside the panel.
    /// </summary>
    /// <param name="band">
    /// The bar's band (验收缺陷 B): the curve's sheet once carried its own
    /// birth Topmost, so it floated above windows the bar was under. WPF's
    /// Topmost is aligned here on every draw — the insert-after handle below
    /// only orders it against the panel, it cannot move a window out of the
    /// band its bit puts it in.
    /// </param>
    public void ShowCurve(ScreenRect card, ScreenRect panel, IntPtr below, ZBand band)
    {
        const int margin = 48;
        var left = Math.Min(card.Left, panel.Left) - margin;
        var top = Math.Min(card.Top, panel.Top) - margin;
        var right = Math.Max(card.Right, panel.Right) + margin;
        var bottom = Math.Max(card.Bottom, panel.Bottom) + margin;

        Topmost = band == ZBand.Topmost;

        var helper = new WindowInteropHelper(this);
        _ = helper.EnsureHandle();

        // Visible first, pressed under the panel second (U-03): a Show() that
        // lands after the z-order write re-raises the sheet above the panel,
        // and the curve then explains itself over the thing it points at.
        // The flash costs nothing — the sheet is transparent until the new
        // curve is drawn below, and it never takes activation.
        if (!IsVisible)
        {
            Show();
        }

        TransientWindow.PlaceBelow(
            helper.Handle, below, left, top, right - left, bottom - top);

        var (scaleX, scaleY) = ScreenGeometry.ScaleForRect(new ScreenRect(left, top, right, bottom));

        var curve = PreviewConnector.Between(card, panel);

        double LocalX(int x) => (x - left) / scaleX;
        double LocalY(int y) => (y - top) / scaleY;

        var figure = new PathFigure(
            new Point(LocalX(curve.From.X), LocalY(curve.From.Y)),
            [new BezierSegment(
                new Point(LocalX(curve.ControlFrom.X), LocalY(curve.ControlFrom.Y)),
                new Point(LocalX(curve.ControlTo.X), LocalY(curve.ControlTo.Y)),
                new Point(LocalX(curve.To.X), LocalY(curve.To.Y)),
                true)],
            false);

        _stroke.Data = new PathGeometry([figure]);
        Center(_fromDot, LocalX(curve.From.X), LocalY(curve.From.Y));
        Center(_toDot, LocalX(curve.To.X), LocalY(curve.To.Y));

        UpdateLayout();
    }

    private static void Center(System.Windows.Shapes.Ellipse dot, double x, double y)
    {
        Canvas.SetLeft(dot, x - dot.Width / 2);
        Canvas.SetTop(dot, y - dot.Height / 2);
    }

    public void HideCurve()
    {
        if (IsVisible)
        {
            Hide();
        }
    }
}
