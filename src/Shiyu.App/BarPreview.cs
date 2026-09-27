using System.Windows;
using System.Windows.Media;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// A window's rectangle in physical pixels, read straight from the OS — the
/// one source that is right on every monitor and scale combination.
/// </summary>
internal static class WindowRects
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static bool TryGet(IntPtr handle, out ScreenRect rect)
    {
        if (handle != IntPtr.Zero && GetWindowRect(handle, out var native))
        {
            rect = new ScreenRect(native.Left, native.Top, native.Right, native.Bottom);
            return true;
        }

        rect = default;
        return false;
    }
}

internal partial class BarWindow
{
    // --- preview panel ---------------------------------------------------------

    /// <summary>
    /// Executes one policy decision. Open and Retarget carry the panel to the
    /// card it belongs beside; Close takes it away. Everything time-based
    /// flows through the tick into here, so there is exactly one code path
    /// that shows and moves the panel.
    /// </summary>
    private void RunPreviewCommand(PreviewCommand command)
    {
        // The panel inherits this window's DPI scale: before its first show it
        // has no source of its own, and a default of 1.0 misplaces it on any
        // scaled desk.
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice
            ?? Matrix.Identity;

        switch (command)
        {
            case PreviewCommand.Open when CardById(_previewPolicy.Card) is { } opening:
                _connectorAnchor = AnchorFor(opening) ?? WindowRect();
                EnsurePreview().ShowFor(opening, _connectorAnchor.Value, slide: false,
                    scaleX: scale.M11, scaleY: scale.M22);
                break;

            case PreviewCommand.Retarget when CardById(_previewPolicy.Card) is { } moving:
                _connectorAnchor = AnchorFor(moving) ?? WindowRect();
                EnsurePreview().ShowFor(moving, _connectorAnchor.Value, slide: true,
                    scaleX: scale.M11, scaleY: scale.M22);
                break;

            case PreviewCommand.Close:
                _preview?.TakeDown();
                _connector?.HideCurve();
                break;
        }
    }

    private ConnectorWindow? _connector;

    /// <summary>The row the panel (and its connector) currently belongs beside.</summary>
    private ScreenRect? _connectorAnchor;

    private PreviewWindow EnsurePreview()
    {
        if (_preview is null)
        {
            _preview = new PreviewWindow(_fileIcons);
            _preview.PointerRestingOnPanel += () => RunPreviewCommand(_previewPolicy.PreviewEntered());
            _preview.PointerLeftPanel += () => RunPreviewCommand(_previewPolicy.PreviewLeft());
            _preview.PanelMoved += OnPreviewPanelMoved;

            // Born into the bar's z-tier (票 39): the panel never hovers above
            // windows the bar itself is under.
            _preview.Topmost = Topmost;
        }

        return _preview;
    }

    /// <summary>
    /// The connector redraws with every step the panel takes — first arrival
    /// included, which is the teleport the ticket asks for: a line flying in
    /// from the previous row's position would read as a glitch, not as craft.
    /// </summary>
    private void OnPreviewPanelMoved(ScreenRect panel)
    {
        if (_connectorAnchor is not { } anchor)
        {
            return;
        }

        _connector ??= new ConnectorWindow();
        _connector.Topmost = Topmost;
        _connector.ShowCurve(anchor, panel, new System.Windows.Interop.WindowInteropHelper(_preview).Handle);
    }

    /// <summary>The panel follows the pointer only between realised cards; off-list the bar anchors it.</summary>
    private BarCard? CardById(long? id)
        => id is { } key ? VisibleRows.FirstOrDefault(card => card.Id == key) : null;

    /// <summary>
    /// The hovered or selected card's rectangle in physical pixels — where the
    /// preview hangs from. Null when the card is not realized (a keyboard move
    /// still scrolling into view); the settle beat usually resolves that.
    ///
    /// Built from the window's own physical rectangle plus the card's offset
    /// inside it, rather than PointToScreen: on a desk with mixed scale
    /// factors, PointToScreen composes through the wrong monitor's transform
    /// and the anchor lands on the wrong screen.
    /// </summary>
    private ScreenRect? AnchorFor(BarCard card)
    {
        var container = PinnedList.ItemContainerGenerator.ContainerFromItem(card);
        if (container is null)
        {
            container = Cards.ItemContainerGenerator.ContainerFromItem(card);
        }

        if (container is not FrameworkElement element || element.ActualWidth <= 0)
        {
            return null;
        }

        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (!WindowRects.TryGet(handle, out var window))
        {
            return null;
        }

        var offset = element.TranslatePoint(new Point(0, 0), this);
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice
            ?? Matrix.Identity;

        var anchorRect = new ScreenRect(
            window.Left + (int)Math.Round(offset.X * scale.M11),
            window.Top + (int)Math.Round(offset.Y * scale.M22),
            window.Left + (int)Math.Round((offset.X + element.ActualWidth) * scale.M11),
            window.Top + (int)Math.Round((offset.Y + element.ActualHeight) * scale.M22));

        return anchorRect;
    }

    private ScreenRect WindowRect()
        => WindowRects.TryGet(new System.Windows.Interop.WindowInteropHelper(this).Handle, out var rect)
            ? rect
            : new ScreenRect(0, 0, 0, 0);
}
