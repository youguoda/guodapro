using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class BadgePlacementTests
{
    private static readonly ScreenRect Screen = new(0, 0, 1920, 1080);
    private const int Width = 120;
    private const int Height = 40;

    [Fact]
    public void It_sits_below_and_right_of_the_cursor_when_there_is_room()
    {
        var placed = BadgePlacement.Place(new ScreenPoint(500, 500), Width, Height, Screen, offset: 18);

        Assert.Equal(new ScreenPoint(518, 518), placed);
    }

    [Fact]
    public void Near_the_right_edge_it_flips_to_the_left_of_the_cursor()
    {
        var placed = BadgePlacement.Place(new ScreenPoint(1900, 500), Width, Height, Screen, offset: 18);

        Assert.Equal(1900 - 18 - Width, placed.X);
        Assert.True(placed.X + Width <= Screen.Right);
    }

    [Fact]
    public void Near_the_bottom_edge_it_flips_above_the_cursor()
    {
        var placed = BadgePlacement.Place(new ScreenPoint(500, 1070), Width, Height, Screen, offset: 18);

        Assert.Equal(1070 - 18 - Height, placed.Y);
        Assert.True(placed.Y + Height <= Screen.Bottom);
    }

    [Fact]
    public void In_the_bottom_right_corner_it_flips_both_ways_at_once()
    {
        var placed = BadgePlacement.Place(new ScreenPoint(1915, 1075), Width, Height, Screen, offset: 18);

        Assert.True(placed.X + Width <= Screen.Right);
        Assert.True(placed.Y + Height <= Screen.Bottom);
    }

    [Fact]
    public void It_never_lands_outside_the_screen_even_when_flipping_overshoots()
    {
        var narrow = new ScreenRect(0, 0, 130, 60);

        var placed = BadgePlacement.Place(new ScreenPoint(125, 55), Width, Height, narrow, offset: 18);

        Assert.InRange(placed.X, narrow.Left, narrow.Right - Width);
        Assert.InRange(placed.Y, narrow.Top, narrow.Bottom - Height);
    }

    [Fact]
    public void A_secondary_monitor_left_of_the_primary_keeps_the_badge_on_that_monitor()
    {
        // Screens to the left of the primary have negative coordinates, which
        // is where naive clamping against 0 puts the badge on the wrong screen.
        var leftMonitor = new ScreenRect(-1920, 0, 0, 1080);

        var placed = BadgePlacement.Place(new ScreenPoint(-960, 540), Width, Height, leftMonitor, offset: 18);

        Assert.InRange(placed.X, leftMonitor.Left, leftMonitor.Right - Width);
        Assert.Equal(-942, placed.X);
    }
}
