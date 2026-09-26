using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class PreviewConnectorTests
{
    private const int CardLeft = 100;
    private const int CardTop = 200;
    private const int CardRight = 500;
    private const int CardBottom = 300;

    private static readonly ScreenRect Card = new(CardLeft, CardTop, CardRight, CardBottom);

    [Fact]
    public void A_panel_to_the_right_attaches_at_the_facing_edges()
    {
        var panel = new ScreenRect(560, 180, 900, 320);

        var curve = PreviewConnector.Between(Card, panel);

        Assert.Equal(CardRight, curve.From.X);
        Assert.InRange(curve.From.Y, CardTop, CardBottom);
        Assert.Equal(panel.Left, curve.To.X);
    }

    [Fact]
    public void A_panel_to_the_left_attaches_at_the_left_edge_of_the_card()
    {
        var panel = new ScreenRect(-400, 180, 40, 320);

        var curve = PreviewConnector.Between(Card, panel);

        Assert.Equal(CardLeft, curve.From.X);
        Assert.Equal(panel.Right, curve.To.X);
    }

    [Fact]
    public void A_panel_below_attaches_at_the_horizontal_edges()
    {
        var panel = new ScreenRect(120, 380, 480, 600);

        var curve = PreviewConnector.Between(Card, panel);

        Assert.Equal(CardBottom, curve.From.Y);
        Assert.Equal(panel.Top, curve.To.Y);
        Assert.InRange(curve.From.X, CardLeft, CardRight);
    }

    [Fact]
    public void A_panel_above_attaches_at_the_top_edge_of_the_card()
    {
        var panel = new ScreenRect(120, -100, 480, 120);

        var curve = PreviewConnector.Between(Card, panel);

        Assert.Equal(CardTop, curve.From.Y);
        Assert.Equal(panel.Bottom, curve.To.Y);
    }

    [Fact]
    public void The_curve_neither_loops_through_the_panel_nor_the_card()
    {
        // The panel overlaps the card's span vertically and sits close: the
        // straight right-edge span is the only one that cannot cross either.
        var panel = new ScreenRect(520, 190, 940, 330);

        var curve = PreviewConnector.Between(Card, panel);

        Assert.Equal(CardRight, curve.From.X);
        Assert.Equal(panel.Left, curve.To.X);

        // Both endpoints on the facing edges, so no sampling probe of the
        // span lands inside either rectangle.
        for (var step = 1; step <= 8; step++)
        {
            var t = step / 9.0;
            var x = curve.From.X + (int)((curve.To.X - curve.From.X) * t);
            Assert.InRange(x, CardRight - 1, panel.Left + 1);
        }
    }

    [Fact]
    public void Horizontal_spans_get_a_lying_s_and_vertical_ones_a_standing_s()
    {
        var side = PreviewConnector.Between(Card, new ScreenRect(560, 180, 900, 320));
        Assert.Equal(side.From.Y, side.ControlFrom.Y);
        Assert.Equal(side.To.Y, side.ControlTo.Y);

        var below = PreviewConnector.Between(Card, new ScreenRect(120, 380, 480, 600));
        Assert.Equal(below.From.X, below.ControlFrom.X);
        Assert.Equal(below.To.X, below.ControlTo.X);
    }

    [Fact]
    public void The_control_points_reach_toward_each_other_not_away()
    {
        var curve = PreviewConnector.Between(Card, new ScreenRect(560, 180, 900, 320));

        Assert.InRange(curve.ControlFrom.X, curve.From.X, curve.To.X);
        Assert.InRange(curve.ControlTo.X, curve.From.X, curve.To.X);
    }

    [Fact]
    public void Degenerate_input_still_yields_a_drawing_curve()
    {
        var same = new ScreenRect(CardLeft, CardTop, CardRight, CardBottom);

        var curve = PreviewConnector.Between(Card, same);

        Assert.NotNull(curve);
    }
}
