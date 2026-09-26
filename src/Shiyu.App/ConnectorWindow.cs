using System.Runtime.InteropServices;
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
        var helper = new WindowInteropHelper(this);
        var style = GetWindowLongPtr(helper.Handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(
            helper.Handle,
            GwlExStyle,
            new IntPtr(style | WsExTransparent | NativeNoActivate));
    }

    /// <summary>
    /// Positions the sheet over the region the curve spans and redraws it.
    /// The card and panel rectangles arrive in physical pixels; the sheet
    /// converts to its own DIPs, which is correct wherever it sits.
    /// </summary>
    public void ShowCurve(ScreenRect card, ScreenRect panel, IntPtr below)
    {
        const int margin = 48;
        var left = Math.Min(card.Left, panel.Left) - margin;
        var top = Math.Min(card.Top, panel.Top) - margin;
        var right = Math.Max(card.Right, panel.Right) + margin;
        var bottom = Math.Max(card.Bottom, panel.Bottom) + margin;

        var helper = new WindowInteropHelper(this);
        _ = helper.EnsureHandle();
        SetWindowPos(helper.Handle, below, left, top, right - left, bottom - top,
            SwpNoActivate | SwpShowWindow);
        UpdateLayout();

        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        if (scale <= 0)
        {
            scale = 1.0;
        }

        var curve = PreviewConnector.Between(card, panel);

        double LocalX(int x) => (x - left) / scale;
        double LocalY(int y) => (y - top) / scale;

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

        if (!IsVisible)
        {
            Show();
        }
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

    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long NativeNoActivate = 0x0800_0000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
