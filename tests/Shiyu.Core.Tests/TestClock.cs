namespace Shiyu.Core.Tests;

/// <summary>A clock the test moves by hand, so timestamps are assertable.</summary>
public sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
