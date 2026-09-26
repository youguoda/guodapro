using Shiyu.Core;
using Shiyu.Core.Tests.Fakes;

namespace Shiyu.Core.Tests;

/// <summary>
/// The recording switches the guide asks about are real gates, not cosmetic
/// checkboxes: off means no entry, ever — and text, the reason the tool
/// exists, has no switch to lose.
/// </summary>
public class OnboardingGatingTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static (ClipboardPipeline Pipeline, FakeClipboardMonitor Monitor, EntryStore Store) Open()
    {
        var store = EntryStore.Open(new TempDatabase().FilePath);
        var monitor = new FakeClipboardMonitor();
        var images = new ImageArchive(Path.Combine(Path.GetTempPath(), "shiyu-gating", Guid.NewGuid().ToString("N")));
        var pipeline = new ClipboardPipeline(monitor, store, new TestClock(Noon), new ExclusionPolicy(), images);
        return (pipeline, monitor, store);
    }

    [Fact]
    public async Task Images_are_not_recorded_when_the_switch_is_off()
    {
        var (pipeline, monitor, store) = Open();
        pipeline.RecordImages = false;

        monitor.EmitImage("图 10×10", "Snipaste");
        await pipeline.Idle;

        Assert.Equal(0, store.Count());
    }

    [Fact]
    public async Task Files_are_not_recorded_when_the_switch_is_off()
    {
        var (pipeline, monitor, store) = Open();
        pipeline.RecordFiles = false;

        monitor.EmitFiles([@"C:\one.txt", @"C:\two.txt"], "explorer");

        Assert.Equal(0, store.Count());
    }

    [Fact]
    public async Task Text_records_regardless_of_the_kind_switches()
    {
        var (pipeline, monitor, store) = Open();
        pipeline.RecordImages = false;
        pipeline.RecordFiles = false;

        monitor.Emit("still here", "ZCode");
        await pipeline.Idle;

        Assert.Equal(1, store.Count());
    }

    [Fact]
    public async Task The_switches_back_on_record_again()
    {
        var (pipeline, monitor, store) = Open();
        pipeline.RecordImages = false;

        pipeline.RecordImages = true;
        monitor.EmitImage("图 4×4", "Snipaste");
        await pipeline.Idle;

        Assert.Equal(1, store.Count());
    }

    [Fact]
    public void Onboarding_defaults_keep_everything_recording_and_run_once()
    {
        var settings = new AppSettings();

        Assert.True(settings.RecordImages);
        Assert.True(settings.RecordFiles);
        Assert.False(settings.OnboardingCompleted);
    }
}
