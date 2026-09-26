namespace Shiyu.Core;

/// <summary>
/// The preview panel's final size, worked out from numbers the caller already
/// has — text wrapped-line count, image pixel size, file row count — so the
/// window opens at the size it will stay (ticket 17: a panel that grows after
/// appearing reads as a guess, not an answer).
///
/// Pure arithmetic: the caller measures the text with the real font (that is a
/// font question, not a layout one) and hands the line count in.
/// </summary>
public static class PreviewSizing
{
    /// <summary>Panel bounds in device-independent units, tuned beside a 360-wide bar.</summary>
    public const double MaxWidth = 480;

    public const double MaxHeight = 560;
    public const double MinWidth = 240;
    public const double MinHeight = 96;

    /// <summary>
    /// What one panel adds around its content: the header row, the paddings,
    /// the border. One pair for every kind, so the kinds stay visually
    /// consistent when the bar flicks between them.
    /// </summary>
    public const double ChromeVertical = 58;

    public const double ChromeHorizontal = 26;

    /// <summary>The panel size for a text entry.</summary>
    /// <param name="lineCount">Wrapped lines at <paramref name="textWidth"/>, measured by the caller.</param>
    /// <param name="textWidth">The width the text was measured against; the widest un-wrapped run when it fits.</param>
    /// <param name="lineHeight">One body line, from the design tokens.</param>
    public static (double Width, double Height) ForText(
        int lineCount, double textWidth, double lineHeight)
    {
        lineCount = Math.Max(1, lineCount);

        // Width follows the text until the panel would outgrow its bounds;
        // height follows the lines, capped where the body starts to scroll.
        var width = Math.Clamp(textWidth + ChromeHorizontal, MinWidth, MaxWidth);
        var height = Math.Clamp(lineCount * lineHeight + ChromeVertical, MinHeight, MaxHeight);

        return (Math.Round(width), Math.Round(height));
    }

    /// <summary>
    /// The panel size for an image entry: the picture scaled to fit the box
    /// whole, panel and all — never a cropped image and never a resize after
    /// the fact, because the pixels' shape is known before anything loads.
    /// </summary>
    public static (double Width, double Height) ForImage(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            // A row that predates the size columns and lost its thumbnail:
            // the smallest honest panel, filled by whatever can be decoded.
            return (MinWidth, MinHeight);
        }

        var boxWidth = MaxWidth - ChromeHorizontal;
        var boxHeight = MaxHeight - ChromeVertical;

        // Fit the box, but never magnify: a 40×40 favicon previews as itself,
        // not as a blur stretched across the panel.
        var scale = Math.Min(Math.Min(boxWidth / pixelWidth, boxHeight / pixelHeight), 1.0);

        var width = pixelWidth * scale + ChromeHorizontal;
        var height = pixelHeight * scale + ChromeVertical;

        return (Math.Round(Math.Clamp(width, MinWidth, MaxWidth)), Math.Round(Math.Clamp(height, MinHeight, MaxHeight)));
    }

    /// <summary>The panel size for a file entry: every row shown, scrolling only past the cap.</summary>
    public static (double Width, double Height) ForFiles(int rowCount, double rowHeight)
    {
        rowCount = Math.Max(1, rowCount);

        // Paths are long and the panel sits beside a card that shows the
        // short names; full width is the useful shape here.
        var height = Math.Clamp(rowCount * rowHeight + ChromeVertical, MinHeight, MaxHeight);

        return (MaxWidth, Math.Round(height));
    }
}
