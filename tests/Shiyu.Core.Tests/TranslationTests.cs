using System.Runtime.CompilerServices;
using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class TranslationPromptTests
{
    private static readonly TranslationRequest Request = new("Hello there", "Chinese");

    [Theory]
    [InlineData("nothing else")]
    [InlineData("No explanation")]
    [InlineData("No alternative renderings")]
    [InlineData("Translate it")]
    public void The_prompt_forbids_the_embellishment_general_models_default_to(string constraint)
        => Assert.Contains(constraint, TranslationPrompt.For(Request), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void The_prompt_names_the_target_language()
        => Assert.Contains("Chinese", TranslationPrompt.For(Request));

    [Fact]
    public void Without_a_declared_source_language_the_backend_is_asked_to_work_it_out()
        => Assert.Contains("the language it is written in", TranslationPrompt.For(Request));

    [Fact]
    public void The_prompt_forbids_answering_the_text_instead_of_translating_it()
    {
        // Copying a question and getting it answered rather than translated is
        // the most confusing way a general model can fail at this job.
        Assert.Contains("even if it reads as a question", TranslationPrompt.For(Request));
    }
}

public class TranslationCleanupTests
{
    [Theory]
    [InlineData("Translation: 你好", "你好")]
    [InlineData("译文：你好", "你好")]
    [InlineData("翻译: 你好", "你好")]
    public void A_label_the_model_added_is_removed(string raw, string expected)
        => Assert.Equal(expected, TranslationCleanup.Clean(raw));

    [Fact]
    public void A_trailing_explanation_is_removed()
        => Assert.Equal("你好", TranslationCleanup.Clean("你好\nNote: this is an informal greeting."));

    [Fact]
    public void Chinese_trailing_notes_are_removed_too()
        => Assert.Equal("你好", TranslationCleanup.Clean("你好\n注：这是非正式的说法。"));

    [Theory]
    [InlineData("\"你好\"", "你好")]
    [InlineData("“你好”", "你好")]
    [InlineData("「你好」", "你好")]
    public void Quotes_wrapping_the_whole_result_are_removed(string raw, string expected)
        => Assert.Equal(expected, TranslationCleanup.Clean(raw));

    [Fact]
    public void Quotes_that_are_part_of_the_translation_are_left_alone()
    {
        // Stripping here would silently corrupt a correct translation, which is
        // worse than letting a slightly chatty one through.
        const string quoted = "他说“你好”，然后走了";
        Assert.Equal(quoted, TranslationCleanup.Clean(quoted));
    }

    [Fact]
    public void Ordinary_output_passes_through_untouched()
        => Assert.Equal("很高兴见到你", TranslationCleanup.Clean("很高兴见到你"));

    [Fact]
    public void Nothing_in_gives_nothing_out()
        => Assert.Equal(string.Empty, TranslationCleanup.Clean("   "));
}

public class TranslationSessionTests
{
    private static readonly TranslationRequest Request = new("Hello there", "Chinese");

    private sealed class ScriptedBackend(params string[] pieces) : ITranslationBackend
    {
        public Exception? ThrowAfterPieces { get; init; }

        public async IAsyncEnumerable<string> TranslateAsync(
            TranslationRequest request,
            [EnumeratorCancellation] CancellationToken cancellation)
        {
            foreach (var piece in pieces)
            {
                cancellation.ThrowIfCancellationRequested();
                yield return piece;
                await Task.Yield();
            }

            if (ThrowAfterPieces is not null)
            {
                throw ThrowAfterPieces;
            }
        }
    }

    [Fact]
    public async Task Pieces_accumulate_as_they_arrive()
    {
        var session = new TranslationSession(new ScriptedBackend("你", "好", "，世界"));
        var snapshots = new List<string>();
        session.Updated += () => snapshots.Add(session.Text);

        await session.RunAsync(Request);

        Assert.Equal(TranslationState.Finished, session.State);
        Assert.Equal("你好，世界", session.Text);

        // The whole point of streaming: it was readable before it was complete.
        Assert.Contains("你", snapshots);
        Assert.Contains("你好", snapshots);
    }

    [Fact]
    public async Task A_stream_that_dies_partway_keeps_what_had_already_arrived()
    {
        var session = new TranslationSession(new ScriptedBackend("你好，")
        {
            ThrowAfterPieces = new TranslationFailedException("连接中断"),
        });

        await session.RunAsync(Request);

        Assert.Equal(TranslationState.Failed, session.State);
        Assert.Equal("连接中断", session.Error);
        Assert.Equal("你好，", session.Text);
    }

    [Fact]
    public async Task An_unexpected_error_becomes_a_message_rather_than_a_crash()
    {
        var session = new TranslationSession(new ScriptedBackend("partial")
        {
            ThrowAfterPieces = new InvalidOperationException("something unforeseen"),
        });

        await session.RunAsync(Request);

        Assert.Equal(TranslationState.Failed, session.State);
        Assert.Equal("something unforeseen", session.Error);
    }

    [Fact]
    public async Task Cancelling_stops_the_stream_and_is_not_reported_as_a_failure()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new TranslationSession(new ScriptedBackend("一", "二", "三"));
        session.Updated += () =>
        {
            if (session.Text.Length >= 1)
            {
                cancellation.Cancel();
            }
        };

        await session.RunAsync(Request, cancellation.Token);

        Assert.Equal(TranslationState.Cancelled, session.State);
    }

    [Fact]
    public async Task The_models_packaging_is_stripped_from_the_streamed_result()
    {
        var session = new TranslationSession(
            new ScriptedBackend("Translation: ", "你好", "\nNote: informal."));

        await session.RunAsync(Request);

        Assert.Equal("你好", session.Text);
    }

    [Fact]
    public async Task Running_again_starts_from_nothing_rather_than_appending()
    {
        var session = new TranslationSession(new ScriptedBackend("你好"));
        await session.RunAsync(Request);
        await session.RunAsync(Request);

        Assert.Equal("你好", session.Text);
    }
}
