namespace Shiyu.Core;

/// <summary>A point in physical screen pixels.</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>A rectangle in physical screen pixels; Right and Bottom are exclusive.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>
/// Works out where a small transient window should sit relative to the cursor.
///
/// In Core because it is arithmetic, and because getting it wrong is invisible
/// until someone copies text near the edge of a screen — which, on a multi-
/// monitor desk, is most of the time.
/// </summary>
public static class BadgePlacement
{
    /// <summary>
    /// Far enough that the badge does not sit under the pointer and swallow the
    /// next click, close enough to read as belonging to it.
    /// </summary>
    public const int CursorOffset = 18;

    public static ScreenPoint Place(
        ScreenPoint cursor, int width, int height, ScreenRect workArea, int offset = CursorOffset)
    {
        // Below and to the right by default: that is where a pointer's own
        // shadow falls, so it reads as attached to the cursor.
        var x = cursor.X + offset;
        var y = cursor.Y + offset;

        // Flip to the other side rather than merely clamping. Clamping would
        // slide the badge under the pointer; flipping keeps it clear of it.
        if (x + width > workArea.Right)
        {
            x = cursor.X - offset - width;
        }

        if (y + height > workArea.Bottom)
        {
            y = cursor.Y - offset - height;
        }

        // Flipping can overshoot the opposite edge on a small screen, so clamp
        // last. A badge half off the screen is worse than one near the pointer.
        x = Math.Clamp(x, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        y = Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));

        return new ScreenPoint(x, y);
    }
}
