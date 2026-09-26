namespace Shiyu.Core;

/// <summary>
/// Where the preview panel sits: beside the card it previews, on whichever
/// side of it has room, always fully inside the monitor's working area.
///
/// Pure sibling of <see cref="BadgePlacement"/>, kept testable for the same
/// reason: near an edge of a multi-monitor desk is where placement bugs live.
/// </summary>
public static class PreviewPlacement
{
    /// <summary>The breathing room between a card and its preview.</summary>
    public const int Gap = 10;

    /// <summary>
    /// Places the panel beside <paramref name="anchor"/> — to its right when
    /// that fits, to its left when only that does, overlapping the anchor
    /// when the working area is narrower than the panel and both.
    /// </summary>
    public static ScreenPoint Place(
        ScreenRect anchor, int width, int height, ScreenRect workArea, int gap = Gap)
    {
        var right = anchor.Right + gap;
        var left = anchor.Left - gap - width;

        int x;
        if (right + width <= workArea.Right)
        {
            x = right;
        }
        else if (left >= workArea.Left)
        {
            x = left;
        }
        else
        {
            // Neither side has room (a bar parked hard against a screen
            // edge, or a panel wider than half a screen): clamp into the
            // work area and let it overlap the list rather than the taskbar.
            x = Math.Clamp(anchor.Right + gap, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        }

        // Top-aligned with the card reads as "this previews that"; the
        // clamps keep it off the taskbar and inside the monitor.
        var y = Math.Clamp(
            anchor.Top,
            workArea.Top,
            Math.Max(workArea.Top, workArea.Bottom - height));

        return new ScreenPoint(x, y);
    }
}
