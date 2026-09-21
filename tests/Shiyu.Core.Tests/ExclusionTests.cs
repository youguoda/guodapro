using Shiyu.Core;
using Shiyu.Core.Tests.Fakes;

namespace Shiyu.Core.Tests;

public class ExclusionTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static (FakeClipboardMonitor Clipboard, EntryStore Store, ClipboardPipeline Pipeline)
        Build(TempDatabase database, ExclusionPolicy exclusions)
    {
        var clipboard = new FakeClipboardMonitor();
        var store = EntryStore.Open(database.FilePath);
        var pipeline = new ClipboardPipeline(clipboard, store, new TestClock(Noon), exclusions);
        return (clipboard, store, pipeline);
    }

    [Fact]
    public void Content_marked_for_exclusion_is_never_recorded()
    {
        using var database = new TempDatabase();
        var (clipboard, store, pipeline) = Build(database, new ExclusionPolicy());
        using var _ = store;
        using var __ = pipeline;

        clipboard.EmitExcluded("hunter2");

        Assert.Empty(store.Recent(limit: 10));
    }

    [Fact]
    public void A_source_app_rule_keeps_that_application_out_of_the_history()
    {
        using var database = new TempDatabase();
        var exclusions = new ExclusionPolicy([ExclusionRule.ForSourceApp("KeePassXC")]);
        var (clipboard, store, pipeline) = Build(database, exclusions);
        using var _ = store;
        using var __ = pipeline;

        clipboard.Emit("a password", sourceApp: "KeePassXC");
        clipboard.Emit("an ordinary note", sourceApp: "notepad");

        var entry = Assert.Single(store.Recent(limit: 10));
        Assert.Equal("an ordinary note", entry.Text);
    }

    [Fact]
    public void Source_app_rules_ignore_case_and_version_suffixes()
    {
        using var database = new TempDatabase();
        var exclusions = new ExclusionPolicy([ExclusionRule.ForSourceApp("1Password")]);
        var (clipboard, store, pipeline) = Build(database, exclusions);
        using var _ = store;
        using var __ = pipeline;

        clipboard.Emit("a password", sourceApp: "1password8");

        Assert.Empty(store.Recent(limit: 10));
    }

    [Fact]
    public void A_content_pattern_rule_keeps_matching_text_out_of_the_history()
    {
        using var database = new TempDatabase();
        var exclusions = new ExclusionPolicy([ExclusionRule.ForContentPattern(@"^otp:\d{6}$")]);
        var (clipboard, store, pipeline) = Build(database, exclusions);
        using var _ = store;
        using var __ = pipeline;

        clipboard.Emit("otp:123456");
        clipboard.Emit("otp:not-a-code");

        var entry = Assert.Single(store.Recent(limit: 10));
        Assert.Equal("otp:not-a-code", entry.Text);
    }

    [Fact]
    public void A_malformed_content_pattern_excludes_nothing_rather_than_everything()
    {
        using var database = new TempDatabase();
        // A rule the user typed wrong must not take the whole history down with
        // it, and must not silently start matching everything either.
        var exclusions = new ExclusionPolicy([ExclusionRule.ForContentPattern("([unclosed")]);
        var (clipboard, store, pipeline) = Build(database, exclusions);
        using var _ = store;
        using var __ = pipeline;

        clipboard.Emit("an ordinary note");

        Assert.Single(store.Recent(limit: 10));
    }

    [Fact]
    public void A_rule_added_while_running_applies_to_the_very_next_copy()
    {
        using var database = new TempDatabase();
        var exclusions = new ExclusionPolicy();
        var (clipboard, store, pipeline) = Build(database, exclusions);
        using var _ = store;
        using var __ = pipeline;

        clipboard.Emit("before the rule", sourceApp: "Bitwarden");
        exclusions.Add(ExclusionRule.ForSourceApp("Bitwarden"));
        clipboard.Emit("after the rule", sourceApp: "Bitwarden");

        var entry = Assert.Single(store.Recent(limit: 10));
        Assert.Equal("before the rule", entry.Text);
    }

    [Fact]
    public void A_removed_rule_stops_applying_immediately()
    {
        using var database = new TempDatabase();
        var rule = ExclusionRule.ForSourceApp("Bitwarden");
        var exclusions = new ExclusionPolicy([rule]);
        var (clipboard, store, pipeline) = Build(database, exclusions);
        using var _ = store;
        using var __ = pipeline;

        exclusions.Remove(rule);
        clipboard.Emit("no longer excluded", sourceApp: "Bitwarden");

        Assert.Single(store.Recent(limit: 10));
    }

    [Fact]
    public void The_shipped_presets_cover_common_password_managers()
    {
        using var database = new TempDatabase();
        var (clipboard, store, pipeline) = Build(database, ExclusionPolicy.WithPresets());
        using var _ = store;
        using var __ = pipeline;

        foreach (var manager in new[] { "KeePassXC", "1Password", "Bitwarden", "LastPass", "Dashlane" })
        {
            clipboard.Emit($"secret from {manager}", sourceApp: manager);
        }

        Assert.Empty(store.Recent(limit: 10));
    }

    [Fact]
    public void The_shipped_presets_leave_ordinary_applications_alone()
    {
        using var database = new TempDatabase();
        var (clipboard, store, pipeline) = Build(database, ExclusionPolicy.WithPresets());
        using var _ = store;
        using var __ = pipeline;

        foreach (var app in new[] { "notepad", "chrome", "Code", "WINWORD", "Compass" })
        {
            clipboard.Emit($"ordinary text from {app}", sourceApp: app);
        }

        Assert.Equal(5, store.Recent(limit: 10).Count);
    }

    [Fact]
    public void Content_with_no_known_source_application_is_still_recorded()
    {
        using var database = new TempDatabase();
        var (clipboard, store, pipeline) = Build(database, ExclusionPolicy.WithPresets());
        using var _ = store;
        using var __ = pipeline;

        clipboard.Emit("copied from something unidentifiable", sourceApp: null);

        Assert.Single(store.Recent(limit: 10));
    }
}
