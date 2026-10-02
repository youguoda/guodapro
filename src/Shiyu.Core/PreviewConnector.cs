namespace Shiyu.Core;

/// <summary>
/// The curve that ties the preview panel to the row it belongs to (ticket 18).
///
/// The shape is a cubic bezier whose endpoints sit on the card and the panel;
/// which edges those are is chosen per arrangement, because a floating panel
/// may sit to either side of, above, or below its row — and a line leaving the
/// wrong edge loops around the panel instead of pointing at the row.
/// </summary>
public static class PreviewConnector
{
    /// <summary>How far the curve bulges toward its destination, as a share of the gap.</summary>
    public const double ControlReach = 0.4;

    /// <summary>
    /// 错位阈值（DIP）：预览与卡片顶的竖直差超过它，连接线才值得解释
    /// （§4.7）；对齐时曲线是多余的，画同色桥。
    /// </summary>
    public const double MisalignedDip = 24;

    /// <summary>缝隙阈值（DIP）：预览与卡片的水平空隙超过它才画曲线；贴着放（8）时画同色桥。</summary>
    public const double GapDip = 16;

    /// <summary>
    /// Whether this arrangement needs the curve at all (§4.7, ticket 20): a
    /// panel sitting squarely beside its row explains itself — the bridge
    /// edge line carries the tie — and only a real offset (clamped off the
    /// screen bottom, squeezed by a narrow work area) is worth a line. The
    /// scales are the TARGET monitor's, supplied by the caller; thresholds
    /// are in DIPs.
    /// </summary>
    public static bool ShouldDrawCurve(ScreenRect card, ScreenRect panel, double scaleX, double scaleY)
    {
        scaleX = Math.Max(scaleX, 0.01);
        scaleY = Math.Max(scaleY, 0.01);

        var misaligned = Math.Abs(card.Top - panel.Top) / scaleY;
        var gap = panel.Left >= card.Right
            ? (panel.Left - card.Right) / scaleX
            : panel.Right <= card.Left
                ? (card.Left - panel.Right) / scaleX
                : 0;

        return misaligned > MisalignedDip || gap > GapDip;
    }

    /// <summary>Endpoints and control points of the connector curve, in physical pixels.</summary>
    public sealed record Curve(ScreenPoint From, ScreenPoint To, ScreenPoint ControlFrom, ScreenPoint ControlTo);

    /// <summary>
    /// Picks the card edge and the facing panel point, and shapes the curve
    /// between them. The pair with the shortest span wins; a span that would
    /// cross through the card or the panel body is disqualified outright —
    /// that line would explain nothing.
    /// </summary>
    public static Curve Between(ScreenRect card, ScreenRect panel)
    {
        var best = (From: Midpoint(card.Left, card.Top, card.Right, card.Top), Attach: Midpoint(panel.Left, panel.Top, panel.Right, panel.Top), Score: double.MaxValue);

        foreach (var candidate in EdgeMidpoints(card))
        {
            var attach = FacingPoint(candidate, panel);
            var score = Distance(candidate, attach) + Crossings(candidate, attach, card, panel);

            if (score < best.Score)
            {
                best = (candidate, attach, score);
            }
        }

        var (from, to) = (best.From, best.Attach);
        var gap = Math.Max(Distance(from, to), 1);
        var reach = (int)Math.Round(gap * ControlReach);

        // Control points extend along the dominant axis: a horizontal pair
        // gets a lying-S, a vertical pair a standing one. That is the whole
        // aesthetic — no looping, no kink at either end.
        if (Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y))
        {
            var sign = Math.Sign(to.X - from.X);
            if (sign == 0)
            {
                sign = 1;
            }

            return new Curve(
                from,
                to,
                new ScreenPoint(from.X + sign * reach, from.Y),
                new ScreenPoint(to.X - sign * reach, to.Y));
        }

        var vertical = Math.Sign(to.Y - from.Y);
        if (vertical == 0)
        {
            vertical = 1;
        }

        return new Curve(
            from,
            to,
            new ScreenPoint(from.X, from.Y + vertical * reach),
            new ScreenPoint(to.X, to.Y - vertical * reach));
    }

    private static IEnumerable<ScreenPoint> EdgeMidpoints(ScreenRect rect)
    {
        yield return Midpoint(rect.Left, rect.Top, rect.Right, rect.Top);
        yield return Midpoint(rect.Left, rect.Bottom, rect.Right, rect.Bottom);
        yield return Midpoint(rect.Left, rect.Top, rect.Left, rect.Bottom);
        yield return Midpoint(rect.Right, rect.Top, rect.Right, rect.Bottom);
    }

    private static ScreenPoint Midpoint(int x1, int y1, int x2, int y2)
        => new((x1 + x2) / 2, (y1 + y2) / 2);

    /// <summary>
    /// The point on the panel's boundary that faces the card anchor: on the
    /// near side edge (clamped to the panel's span) when the panel is mostly
    /// beside the card, on the near horizontal edge when it is mostly above
    /// or below.
    /// </summary>
    private static ScreenPoint FacingPoint(ScreenPoint from, ScreenRect panel)
    {
        var centre = new ScreenPoint((panel.Left + panel.Right) / 2, (panel.Top + panel.Bottom) / 2);
        var dx = centre.X - from.X;
        var dy = centre.Y - from.Y;

        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            var x = dx >= 0 ? panel.Left : panel.Right;
            return new ScreenPoint(x, Math.Clamp(from.Y, panel.Top + 2, panel.Bottom - 2));
        }

        var y = dy >= 0 ? panel.Top : panel.Bottom;
        return new ScreenPoint(Math.Clamp(from.X, panel.Left + 2, panel.Right - 2), y);
    }

    private static double Distance(ScreenPoint a, ScreenPoint b)
        => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));

    /// <summary>
    /// A span that enters the card or the panel anywhere but at its own two
    /// endpoints is not a connector, it is a scribble — such candidates carry
    /// a cost that dwarfs any distance.
    /// </summary>
    private static double Crossings(ScreenPoint from, ScreenPoint to, ScreenRect card, ScreenRect panel)
    {
        const double penalty = 1_000_000;

        var hitsCard = SegmentIntersectsRect(from, to, card, skipEndsIn: card);
        var hitsPanel = SegmentIntersectsRect(from, to, panel, skipEndsIn: panel);

        return (hitsCard ? penalty : 0) + (hitsPanel ? penalty : 0);
    }

    private static bool SegmentIntersectsRect(ScreenPoint from, ScreenPoint to, ScreenRect rect, ScreenRect skipEndsIn)
    {
        // Sampled rather than exact: eight probes along the span catch any
        // line that meaningfully enters the rectangle, and the two candidates
        // whose endpoints legitimately sit on this rectangle are excused.
        for (var step = 1; step <= 8; step++)
        {
            var t = step / 9.0;
            var x = (int)Math.Round(from.X + (to.X - from.X) * t);
            var y = (int)Math.Round(from.Y + (to.Y - from.Y) * t);

            var inside = x > rect.Left && x < rect.Right && y > rect.Top && y < rect.Bottom;
            if (!inside)
            {
                continue;
            }

            // An endpoint that belongs to this rectangle may aim inward
            // briefly without it being a crossing.
            var fromBelongs = BelongsTo(from, skipEndsIn);
            var toBelongs = BelongsTo(to, skipEndsIn);

            if (fromBelongs && t > 0.45)
            {
                return true;
            }

            if (toBelongs && t < 0.55)
            {
                return true;
            }

            if (!fromBelongs && !toBelongs)
            {
                return true;
            }
        }

        return false;
    }

    private static bool BelongsTo(ScreenPoint point, ScreenRect rect)
        => point.X >= rect.Left - 1 && point.X <= rect.Right + 1
            && point.Y >= rect.Top - 1 && point.Y <= rect.Bottom + 1;
}
