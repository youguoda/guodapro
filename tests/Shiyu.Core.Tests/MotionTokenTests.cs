using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// Motion is a budget, not a buffet: two duration tiers, two curves, and one
/// switch that turns every effect off together. These tests keep that true as
/// animations accrete — the reference implementation's lesson was that "reduce
/// motion" done per-call gets missed, and the users it harms do not report it.
/// </summary>
public class MotionTokenTests
{
    [Fact]
    public void There_are_exactly_two_curves()
        => Assert.Equal(2, Enum.GetValues<MotionCurve>().Length);

    [Fact]
    public void The_fast_tier_fits_inside_a_blink()
    {
        Assert.True(DesignTokens.MotionFast > TimeSpan.Zero);
        Assert.True(
            DesignTokens.MotionFast <= TimeSpan.FromMilliseconds(200),
            $"状态变化档 {DesignTokens.MotionFast.TotalMilliseconds}ms 超过 200ms 的感知预算");
    }

    [Fact]
    public void The_feature_tier_is_slower_but_still_prompt()
    {
        Assert.True(DesignTokens.MotionSlow > DesignTokens.MotionFast);
        Assert.True(
            DesignTokens.MotionSlow <= TimeSpan.FromMilliseconds(450),
            $"主角时刻档 {DesignTokens.MotionSlow.TotalMilliseconds}ms 让人等了");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reducing_motion_kills_both_tiers(bool feature)
        => Assert.Equal(TimeSpan.Zero, MotionPlan.Duration(animationsAllowed: false, feature: feature));

    [Fact]
    public void State_changes_use_the_fast_tier_and_feature_moments_the_slow_one()
    {
        Assert.Equal(
            DesignTokens.MotionFast,
            MotionPlan.Duration(animationsAllowed: true, feature: false));
        Assert.Equal(
            DesignTokens.MotionSlow,
            MotionPlan.Duration(animationsAllowed: true, feature: true));
    }
}
