using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class PreviewSizingTests
{
    private const double LineHeight = 24;

    [Fact]
    public void A_short_text_gets_a_panel_around_its_own_shape()
    {
        var (width, height) = PreviewSizing.ForText(lineCount: 2, textWidth: 300, lineHeight: LineHeight);

        Assert.Equal(300 + PreviewSizing.ChromeHorizontal, width);
        Assert.Equal(2 * LineHeight + PreviewSizing.ChromeVertical, height);
    }

    [Fact]
    public void A_narrow_text_is_still_wide_enough_to_read_as_a_panel()
    {
        var (width, _) = PreviewSizing.ForText(lineCount: 1, textWidth: 12, lineHeight: LineHeight);

        Assert.Equal(PreviewSizing.MinWidth, width);
    }

    [Fact]
    public void A_long_text_caps_at_both_bounds_instead_of_growing()
    {
        var (width, height) = PreviewSizing.ForText(lineCount: 400, textWidth: 5000, lineHeight: LineHeight);

        Assert.Equal(PreviewSizing.MaxWidth, width);
        Assert.Equal(PreviewSizing.MaxHeight, height);
    }

    [Fact]
    public void An_image_keeps_its_aspect_inside_the_box()
    {
        var (width, height) = PreviewSizing.ForImage(1000, 500);

        var imageWidth = width - PreviewSizing.ChromeHorizontal;
        var imageHeight = height - PreviewSizing.ChromeVertical;

        Assert.Equal(2.0, imageWidth / imageHeight, precision: 2);
        Assert.InRange(width, PreviewSizing.MinWidth, PreviewSizing.MaxWidth);
        Assert.InRange(height, PreviewSizing.MinHeight, PreviewSizing.MaxHeight);
    }

    [Fact]
    public void A_tall_portrait_image_is_limited_by_height_not_width()
    {
        var (width, height) = PreviewSizing.ForImage(500, 3000);

        Assert.Equal(PreviewSizing.MaxHeight, height);
        // Scaled to fit 3000 into the height box, then chrome added.
        Assert.True(width < PreviewSizing.MaxWidth, "a portrait image should not stretch the panel wide");
    }

    [Fact]
    public void A_small_image_is_not_blown_up_beyond_the_panel_itself()
    {
        // A 200×120 image fits the box outright; the panel wraps it, and the
        // minimum keeps the chrome from collapsing onto it.
        var (width, height) = PreviewSizing.ForImage(200, 120);

        Assert.InRange(width, PreviewSizing.MinWidth, PreviewSizing.MaxWidth);
        Assert.InRange(height, PreviewSizing.MinHeight, PreviewSizing.MaxHeight);
    }

    [Fact]
    public void A_row_without_any_size_falls_back_to_the_minimum_panel()
    {
        Assert.Equal(
            (PreviewSizing.MinWidth, PreviewSizing.MinHeight),
            PreviewSizing.ForImage(0, 0));
    }

    [Fact]
    public void A_file_list_scales_with_its_rows_and_scrolls_past_the_cap()
    {
        var (smallWidth, smallHeight) = PreviewSizing.ForFiles(rowCount: 3, rowHeight: 22);
        var (largeWidth, largeHeight) = PreviewSizing.ForFiles(rowCount: 300, rowHeight: 22);

        Assert.Equal(3 * 22 + PreviewSizing.ChromeVertical, smallHeight);
        Assert.Equal(PreviewSizing.MaxWidth, smallWidth);
        Assert.Equal(PreviewSizing.MaxHeight, largeHeight);
    }
}
