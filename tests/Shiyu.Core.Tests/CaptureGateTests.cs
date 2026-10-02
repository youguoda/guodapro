using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class CaptureGateTests
{
    private static ExclusionPolicy PolicyWithPreset()
        => new([ExclusionRule.ForSourceApp("KeePass")]);

    [Fact]
    public void A_foreground_app_on_the_list_blocks_the_capture()
    {
        Assert.True(CaptureGate.BlocksForeground(PolicyWithPreset(), "KeePass"));
        Assert.True(CaptureGate.BlocksForeground(PolicyWithPreset(), "keepassxc")); // case-insensitive
        Assert.True(CaptureGate.BlocksForeground(PolicyWithPreset(), "KeePass2.x"));
    }

    [Fact]
    public void Everything_else_passes()
    {
        Assert.False(CaptureGate.BlocksForeground(PolicyWithPreset(), "notepad"));
        Assert.False(CaptureGate.BlocksForeground(PolicyWithPreset(), null));
        Assert.False(CaptureGate.BlocksForeground(PolicyWithPreset(), ""));
        Assert.False(CaptureGate.BlocksForeground(new ExclusionPolicy(), "KeePass"));
    }

    [Fact]
    public void Content_rules_do_not_block_a_capture_there_is_no_content_yet()
    {
        var policy = new ExclusionPolicy([new ExclusionRule(ExclusionRuleKind.ContentPattern, "secret")]);

        Assert.False(CaptureGate.BlocksForeground(policy, "notepad"));
    }

    [Fact]
    public void Entries_from_excluded_apps_are_split_out_and_counted()
    {
        var policy = PolicyWithPreset();
        var morning = new Entry(1, "alpha", "KeePass", DateTimeOffset.UtcNow);
        var afternoon = new Entry(2, "beta", "notepad", DateTimeOffset.UtcNow);
        var unknown = new Entry(3, "gamma", null, DateTimeOffset.UtcNow);

        var (sendable, skipped) = CaptureGate.SplitSendable([morning, afternoon, unknown], policy);

        Assert.Equal([afternoon, unknown], sendable);
        Assert.Equal(1, skipped);
    }

    [Fact]
    public void An_all_excluded_selection_leaves_nothing_to_send()
    {
        var policy = PolicyWithPreset();
        var entries = new[]
        {
            new Entry(1, "a", "KeePass", DateTimeOffset.UtcNow),
            new Entry(2, "b", "KeePassXC", DateTimeOffset.UtcNow),
        };

        var (sendable, skipped) = CaptureGate.SplitSendable(entries, policy);

        Assert.Empty(sendable);
        Assert.Equal(2, skipped);
    }
}
