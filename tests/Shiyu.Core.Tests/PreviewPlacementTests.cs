using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class PreviewPlacementTests
{
    private static readonly ScreenRect Screen = new(0, 0, 1920, 1080);
    private static readonly ScreenRect WorkArea = new(0, 0, 1920, 1040);
    private const int Width = 480;
    private const int Height = 300;

    [Fact]
    public void It_sits_to_the_right_of_the_card_top_aligned()
    {
        var card = new ScreenRect(100, 200, 460, 280);

        var placed = PreviewPlacement.Place(card, Width, Height, WorkArea);

        Assert.Equal(card.Right + PreviewPlacement.Gap, placed.X);
        Assert.Equal(card.Top, placed.Y);
    }

    [Fact]
    public void Near_the_right_edge_it_takes_the_left_side_instead()
    {
        var card = new ScreenRect(1600, 200, 1900, 280);

        var placed = PreviewPlacement.Place(card, Width, Height, WorkArea);

        Assert.Equal(card.Left - PreviewPlacement.Gap - Width, placed.X);
    }

    [Fact]
    public void A_card_low_on_the_screen_lifts_the_panel_back_into_the_work_area()
    {
        var card = new ScreenRect(100, 900, 400, 980);

        var placed = PreviewPlacement.Place(card, Width, Height, WorkArea);

        Assert.Equal(WorkArea.Bottom - Height, placed.Y);
    }

    [Fact]
    public void A_work_area_narrower_than_the_panel_still_shows_all_of_it()
    {
        var narrow = new ScreenRect(0, 0, 600, 800);
        var card = new ScreenRect(280, 200, 580, 280);

        var placed = PreviewPlacement.Place(card, Width, Height, narrow);

        Assert.InRange(placed.X, narrow.Left, narrow.Right - Width);
        Assert.InRange(placed.Y, narrow.Top, narrow.Bottom - Height);
    }

    [Fact]
    public void A_monitor_left_of_the_primary_keeps_the_panel_on_it()
    {
        var leftMonitor = new ScreenRect(-1920, 0, 0, 1040);
        var card = new ScreenRect(-1500, 300, -1000, 380);

        var placed = PreviewPlacement.Place(card, Width, Height, leftMonitor);

        Assert.InRange(placed.X, leftMonitor.Left, leftMonitor.Right - Width);
        Assert.InRange(placed.Y, leftMonitor.Top, leftMonitor.Bottom - Height);
    }
}
