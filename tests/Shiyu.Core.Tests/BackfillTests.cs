using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// O-36's two background-result guards: the generation counter that drops
/// late image backfills, and the TTL cache that keeps file-existence verdicts
/// off the render path.
/// </summary>
public class BackfillGateTests
{
    [Fact]
    public void A_result_from_the_current_generation_lands()
    {
        var gate = new BackfillGate();

        var seen = gate.Epoch;

        Assert.True(gate.IsCurrent(seen));
    }

    [Fact]
    public void A_result_from_a_generation_the_owner_declared_over_is_dropped()
    {
        var gate = new BackfillGate();

        var seen = gate.Epoch;
        gate.Invalidate();

        Assert.False(gate.IsCurrent(seen));

        var fresh = gate.Epoch;
        Assert.NotEqual(seen, fresh);
        Assert.True(gate.IsCurrent(fresh));
    }

    [Fact]
    public void Parallel_captures_within_one_generation_all_stay_current()
    {
        // The list rebuilds, not each card: many decodes in flight for one
        // generation must all be allowed to land — dropping all but the last
        // would leave every row but one a placeholder forever.
        var gate = new BackfillGate();

        var first = gate.Epoch;
        var second = gate.Epoch;

        Assert.True(gate.IsCurrent(first));
        Assert.True(gate.IsCurrent(second));
    }

    [Fact]
    public void Invalidation_storms_never_resurrect_a_generation()
    {
        var gate = new BackfillGate();
        var seen = gate.Epoch;

        var threads = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
        {
            for (var i = 0; i < 10_000; i++)
            {
                gate.Invalidate();
            }
        })).ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        // The generation moved an unknown number of times, never backwards:
        // the captured one is gone whatever the count was.
        Assert.False(gate.IsCurrent(seen));
        Assert.True(gate.IsCurrent(gate.Epoch));
    }
}

public class FileExistenceCacheTests
{
    private sealed class Clock
    {
        public long Now;

        public long Get() => Now;

        public void Advance(long milliseconds) => Now += milliseconds;
    }

    private static (FileExistenceCache Cache, Clock Time) Make(TimeSpan? ttl = null)
    {
        var time = new Clock();
        return (new FileExistenceCache(ttl, time.Get), time);
    }

    [Fact]
    public void A_path_never_probed_has_no_verdict_and_wants_a_probe()
    {
        var (cache, _) = Make();

        Assert.Null(cache.Lookup(@"C:\somewhere\file.txt"));
        Assert.True(cache.WantsProbe(@"C:\somewhere\file.txt"));
    }

    [Fact]
    public void A_recorded_verdict_answers_from_memory_until_it_expires()
    {
        var (cache, time) = Make(ttl: TimeSpan.FromSeconds(15));

        cache.Record(@"C:\gone.txt", exists: false, isDirectory: false);

        Assert.Equal(new FileVerdict(false, false), cache.Lookup(@"C:\gone.txt"));
        Assert.False(cache.WantsProbe(@"C:\gone.txt"));

        time.Advance(14_000);
        Assert.NotNull(cache.Lookup(@"C:\gone.txt"));

        time.Advance(2_000);
        Assert.Null(cache.Lookup(@"C:\gone.txt"));
        Assert.True(cache.WantsProbe(@"C:\gone.txt"));
    }

    [Fact]
    public void A_directory_verdict_round_trips()
    {
        var (cache, _) = Make();

        cache.Record(@"C:\somewhere", exists: true, isDirectory: true);

        var verdict = cache.Lookup(@"C:\somewhere");
        Assert.True(verdict?.Exists);
        Assert.True(verdict?.IsDirectory);
    }

    [Fact]
    public void Paths_are_compared_the_way_windows_compares_them()
    {
        var (cache, _) = Make();

        cache.Record(@"C:\Case\FILE.txt", exists: true, isDirectory: false);

        Assert.NotNull(cache.Lookup(@"c:\case\file.txt"));
    }

    [Fact]
    public void Invalidation_throws_the_verdict_away_even_when_fresh()
    {
        var (cache, _) = Make();

        cache.Record(@"C:\just-deleted.txt", exists: true, isDirectory: false);
        cache.Invalidate(@"C:\just-deleted.txt");

        Assert.Null(cache.Lookup(@"C:\just-deleted.txt"));
        Assert.True(cache.WantsProbe(@"C:\just-deleted.txt"));
    }

    [Fact]
    public void Concurrent_reads_and_records_stay_consistent()
    {
        var (cache, _) = Make();
        var path = @"C:\shared.txt";

        var writers = Enumerable.Range(0, 3).Select(_ => new Thread(() =>
        {
            for (var i = 0; i < 5_000; i++)
            {
                cache.Record(path, exists: i % 2 == 0, isDirectory: false);
            }
        })).ToList();

        writers.ForEach(t => t.Start());
        writers.ForEach(t => t.Join());

        // Whatever won the race, the cache holds one whole verdict — a file
        // verdict, never torn — and reads never threw along the way.
        var verdict = cache.Lookup(path);
        Assert.NotNull(verdict);
        Assert.False(verdict.Value.IsDirectory);
    }
}

public class ThemeFollowPolicyTests
{
    [Fact]
    public void The_colour_group_matters_while_following_the_system()
    {
        Assert.True(ThemeFollowPolicy.ShouldRecheck(AppTheme.System, ThemeFollowPolicy.ColourArea));
    }

    [Fact]
    public void An_unlabelled_broadcast_is_treated_as_possible_news()
    {
        // Parts of the shell still send WM_SETTINGCHANGE with no area string;
        // a theme flip among them must not be ignored for politeness.
        Assert.True(ThemeFollowPolicy.ShouldRecheck(AppTheme.System, null));
    }

    [Fact]
    public void Other_setting_groups_are_not_the_theme()
    {
        Assert.False(ThemeFollowPolicy.ShouldRecheck(AppTheme.System, "Wallpaper"));
        Assert.False(ThemeFollowPolicy.ShouldRecheck(AppTheme.System, "Locale"));
    }

    [Fact]
    public void A_fixed_theme_never_follows_the_system()
    {
        Assert.False(ThemeFollowPolicy.ShouldRecheck(AppTheme.Light, ThemeFollowPolicy.ColourArea));
        Assert.False(ThemeFollowPolicy.ShouldRecheck(AppTheme.Dark, null));
    }
}
