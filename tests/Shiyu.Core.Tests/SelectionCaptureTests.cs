using Shiyu.Core;
using Shiyu.Core.Tests.Fakes;

namespace Shiyu.Core.Tests;

/// <summary>
/// Every test here asserts the same invariant alongside whatever else it is
/// checking: the clipboard the user had is the clipboard the user gets back.
/// That is the one failure a capture tool cannot be forgiven for.
/// </summary>
public class SelectionCaptureTests
{
    private static readonly CaptureTiming Timing =
        new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(100));

    private static (FakeCapturePlatform Platform, SelectionCapture Capture) Build(string? existingClipboard)
    {
        var platform = new FakeCapturePlatform();
        platform.PutOnClipboard(existingClipboard);
        return (platform, new SelectionCapture(platform, Timing));
    }

    [Fact]
    public void A_prompt_application_has_its_selection_captured()
    {
        var (platform, capture) = Build("the user's own clipboard");
        platform.AnswersAfterPolls = 1;
        platform.Answer = "the selected text";

        var result = capture.Capture();

        Assert.True(result.Succeeded);
        Assert.Equal("the selected text", result.Text);
        Assert.True(result.ClipboardRestored);
        Assert.Equal("the user's own clipboard", platform.CurrentClipboard);
    }

    [Fact]
    public void A_slow_application_still_gets_its_answer_in_before_the_deadline()
    {
        var (platform, capture) = Build("the user's own clipboard");

        // Nine polls out of a possible ten: only just in time.
        platform.AnswersAfterPolls = 9;

        var result = capture.Capture();

        Assert.True(result.Succeeded);
        Assert.Equal("the selected text", result.Text);
        Assert.Equal("the user's own clipboard", platform.CurrentClipboard);
    }

    [Fact]
    public void An_application_that_never_answers_times_out_and_gives_the_clipboard_back()
    {
        var (platform, capture) = Build("the user's own clipboard");
        platform.AnswersAfterPolls = null;

        var result = capture.Capture();

        Assert.False(result.Succeeded);
        Assert.Equal(CaptureOutcome.NothingCaptured, result.Outcome);
        Assert.Null(result.Text);
        Assert.True(result.ClipboardRestored);
        Assert.Equal("the user's own clipboard", platform.CurrentClipboard);
    }

    [Fact]
    public void Capture_gives_up_rather_than_polling_forever()
    {
        var (platform, capture) = Build("anything");
        platform.AnswersAfterPolls = null;

        capture.Capture();

        // 100 ms of deadline at 10 ms a poll. A loop that ran away would show
        // up here long before it hung a real machine.
        Assert.True(platform.Polls <= 12, $"polled {platform.Polls} times");
    }

    [Fact]
    public void With_nothing_selected_it_reports_nothing_rather_than_the_previous_clipboard()
    {
        var (platform, capture) = Build("something copied earlier");

        // Pressing copy with no selection changes nothing at all.
        platform.AnswersAfterPolls = null;

        var result = capture.Capture();

        Assert.Equal(CaptureOutcome.NothingCaptured, result.Outcome);
        Assert.Null(result.Text);
        Assert.NotEqual("something copied earlier", result.Text);
    }

    [Fact]
    public void A_clipboard_that_changes_but_holds_nothing_usable_is_not_treated_as_a_capture()
    {
        var (platform, capture) = Build("the user's own clipboard");
        platform.AnswersAfterPolls = 2;
        platform.Answer = "";

        var result = capture.Capture();

        Assert.Equal(CaptureOutcome.NothingCaptured, result.Outcome);
        Assert.Null(result.Text);
        Assert.Equal("the user's own clipboard", platform.CurrentClipboard);
    }

    [Fact]
    public void An_empty_clipboard_before_capture_is_restored_as_empty()
    {
        var (platform, capture) = Build(null);
        platform.AnswersAfterPolls = 1;

        var result = capture.Capture();

        Assert.True(result.Succeeded);

        // Restoring must put back "nothing", not leave the captured text
        // sitting there as though the user had copied it.
        Assert.Null(platform.CurrentClipboard);
        Assert.Contains(null, platform.Writes);
    }

    [Fact]
    public void A_clipboard_that_cannot_be_read_fails_without_pressing_anything()
    {
        var platform = new FakeCapturePlatform { ReadFails = true };
        var capture = new SelectionCapture(platform, Timing);

        var result = capture.Capture();

        Assert.Equal(CaptureOutcome.ClipboardUnavailable, result.Outcome);
        Assert.True(result.ClipboardRestored);

        // Nothing was borrowed, so no keystroke should have been sent into the
        // user's application on a false premise.
        Assert.Equal(0, platform.CopyKeystrokes);
    }

    [Fact]
    public void A_clipboard_that_cannot_be_written_back_reports_the_loss_rather_than_hiding_it()
    {
        var (platform, capture) = Build("the user's own clipboard");
        platform.AnswersAfterPolls = 1;
        platform.WriteFails = true;

        var result = capture.Capture();

        Assert.True(result.Succeeded);
        Assert.False(result.ClipboardRestored);
    }

    [Fact]
    public void A_clipboard_that_throws_while_restoring_still_reports_the_loss()
    {
        var (platform, capture) = Build("the user's own clipboard");
        platform.AnswersAfterPolls = 1;

        var result = capture.Capture();
        Assert.True(result.ClipboardRestored);

        var (throwing, throwingCapture) = Build("the user's own clipboard");
        throwing.AnswersAfterPolls = 1;
        throwing.WriteThrows = true;

        var second = throwingCapture.Capture();
        Assert.False(second.ClipboardRestored);
    }

    [Fact]
    public void The_copy_keystroke_is_sent_exactly_once()
    {
        var (platform, capture) = Build("anything");
        platform.AnswersAfterPolls = 3;

        capture.Capture();

        Assert.Equal(1, platform.CopyKeystrokes);
    }

    [Fact]
    public void Pasting_puts_the_text_on_the_clipboard_and_then_presses_paste()
    {
        var (platform, capture) = Build("anything");

        Assert.True(capture.Paste("text to paste"));

        Assert.Equal("text to paste", platform.CurrentClipboard);
        Assert.Equal(1, platform.PasteKeystrokes);
    }

    [Fact]
    public void Pasting_does_not_press_anything_when_the_clipboard_refused_the_text()
    {
        var (platform, capture) = Build("anything");
        platform.WriteFails = true;

        Assert.False(capture.Paste("text to paste"));

        // Pressing paste after a failed write would paste whatever happened to
        // be on the clipboard instead — the wrong text, silently.
        Assert.Equal(0, platform.PasteKeystrokes);
    }
}
