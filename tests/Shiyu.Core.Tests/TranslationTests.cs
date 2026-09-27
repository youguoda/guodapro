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

public class TranslationEchoRetryTests
{
    /// <summary>
    /// 每次调用吐一组片段；一组为 null 表示该次调用直接失败。
    /// </summary>
    private sealed class ScriptedBackend(params string[]?[] calls) : ITranslationBackend
    {
        private readonly Queue<string[]?> _calls = new(calls);

        public List<TranslationRequest> Requests { get; } = [];

        public async IAsyncEnumerable<string> TranslateAsync(
            TranslationRequest request,
            [EnumeratorCancellation] CancellationToken cancellation)
        {
            Requests.Add(request);
            var pieces = _calls.Dequeue();
            if (pieces is null)
            {
                throw new TranslationFailedException("服务不可用");
            }

            foreach (var piece in pieces)
            {
                yield return piece;
                await Task.Yield();
            }
        }
    }

    [Fact]
    public async Task An_echo_with_an_undirected_source_triggers_one_direction_swap_retry()
    {
        // 最常见的回声形态：中文原文配上中文目标——prompt 允许"已是
        // 目标语言就原样返回"，模型恰恰是在"正确地"偷懒。
        var backend = new ScriptedBackend(["你好世界"], ["Hello world"]);
        var session = new TranslationSession(backend);

        await session.RunAsync(new TranslationRequest("你好世界", "Chinese"));

        Assert.Equal(TranslationState.Finished, session.State);
        Assert.Equal("Hello world", session.Text);
        Assert.Equal(2, backend.Requests.Count);

        // 换向：旧目标成为声明的源语言，新目标取原文的本地先验（中文
        // 与旧目标同名，退到面板手动换向同款的 English 缺省）。
        var retry = backend.Requests[1];
        Assert.Equal("你好世界", retry.Text);
        Assert.Equal("Chinese", retry.SourceLanguage);
        Assert.Equal("English", retry.TargetLanguage);
        Assert.Equal(retry, session.CurrentRequest);
    }

    [Fact]
    public async Task An_echo_with_a_user_forced_source_language_is_shown_as_is()
    {
        // 用户钉死了方向，回声就是模型在这个方向上的答案；替用户改方向
        // 是替用户改主意。
        var backend = new ScriptedBackend(["Hello there"]);
        var session = new TranslationSession(backend);

        await session.RunAsync(
            new TranslationRequest("Hello there", "Chinese") { SourceLanguage = "English" });

        Assert.Equal(TranslationState.Finished, session.State);
        Assert.Equal("Hello there", session.Text);
        Assert.Single(backend.Requests);
    }

    [Fact]
    public async Task A_retry_that_echoes_again_is_shown_as_is()
    {
        // 重试只有一次：再回声就如实展示，循环重试没有收尾。
        var backend = new ScriptedBackend(["你好世界"], ["你好世界"]);
        var session = new TranslationSession(backend);

        await session.RunAsync(new TranslationRequest("你好世界", "Chinese"));

        Assert.Equal(TranslationState.Finished, session.State);
        Assert.Equal("你好世界", session.Text);
        Assert.Equal(2, backend.Requests.Count);
    }

    [Fact]
    public async Task An_ordinary_translation_never_triggers_a_retry()
    {
        var backend = new ScriptedBackend(["你", "好，世界"]);
        var session = new TranslationSession(backend);

        await session.RunAsync(new TranslationRequest("Hello there", "Chinese"));

        Assert.Equal(TranslationState.Finished, session.State);
        Assert.Equal("你好，世界", session.Text);
        Assert.Single(backend.Requests);
    }

    [Fact]
    public async Task A_failed_attempt_is_never_retried()
    {
        // 退避重试是后端的职责；会话层不为失败做方向文章。
        var backend = new ScriptedBackend((string[]?)null);
        var session = new TranslationSession(backend);

        await session.RunAsync(new TranslationRequest("Hello there", "Chinese"));

        Assert.Equal(TranslationState.Failed, session.State);
        Assert.Single(backend.Requests);
    }

    [Fact]
    public async Task The_retry_stream_replaces_the_echo_on_screen_from_scratch()
    {
        // 面板跟着 Updated 重画全文：第二次尝试必须从空白开始，否则
        // 回声会残留在译文开头。
        var backend = new ScriptedBackend(["你好世界"], ["Hello world"]);
        var session = new TranslationSession(backend);
        var seen = new List<string>();
        session.Updated += () => seen.Add(session.Text);

        await session.RunAsync(new TranslationRequest("你好世界", "Chinese"));

        Assert.Equal("Hello world", seen[^1]);
        Assert.DoesNotContain("你好世界Hello world", seen);
    }
}
