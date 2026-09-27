using System.Runtime.CompilerServices;

namespace Shiyu.Core;

public enum TranslationState
{
    Idle,
    Streaming,
    Finished,

    /// <summary>Stopped early. Whatever arrived before it stopped is kept.</summary>
    Failed,

    Cancelled,
}

/// <summary>
/// Drives one translation and holds what has arrived so far.
///
/// The partial text is kept on every ending, including failure: a stream that
/// dies two thirds of the way through has still given the user two thirds of
/// what they wanted, and throwing that away to show an error box instead would
/// be a second failure on top of the first.
/// </summary>
public sealed class TranslationSession(ITranslationBackend backend)
{
    private readonly List<string> _pieces = [];

    public TranslationState State { get; private set; } = TranslationState.Idle;

    /// <summary>Set when <see cref="State"/> is <see cref="TranslationState.Failed"/>.</summary>
    public string? Error { get; private set; }

    /// <summary>What has arrived so far, cleaned of any wrapping the model added.</summary>
    public string Text => TranslationCleanup.Clean(string.Concat(_pieces));

    /// <summary>
    /// The request behind the text currently on screen. With an echo retry it
    /// differs from the request passed to <see cref="RunAsync"/> — the direction
    /// label needs to see the swap, not the user's original ask.
    /// </summary>
    public TranslationRequest? CurrentRequest { get; private set; }

    /// <summary>Raised whenever <see cref="Text"/> grows, so a view can follow along.</summary>
    public event Action? Updated;

    public async Task RunAsync(TranslationRequest request, CancellationToken cancellation = default)
    {
        await AttemptAsync(request, cancellation);

        // 回声且用户未强制源语言：换向重试恰好一次。重试请求带着声明的
        // 源语言，即使这里判断写错也构不成循环。再回声就如实展示。
        if (State == TranslationState.Finished
            && request.SourceLanguage is null
            && TranslationEcho.IsEchoish(request.Text, Text)
            && EchoRetryDirection(request) is { } retry)
        {
            await AttemptAsync(retry, cancellation);
        }
    }

    private async Task AttemptAsync(TranslationRequest request, CancellationToken cancellation)
    {
        _pieces.Clear();
        Error = null;
        CurrentRequest = request;
        State = TranslationState.Streaming;
        Updated?.Invoke();

        try
        {
            await foreach (var piece in backend.TranslateAsync(request, cancellation))
            {
                if (piece.Length == 0)
                {
                    continue;
                }

                _pieces.Add(piece);
                Updated?.Invoke();
            }

            State = TranslationState.Finished;
        }
        catch (OperationCanceledException)
        {
            State = TranslationState.Cancelled;
        }
        catch (TranslationFailedException failure)
        {
            State = TranslationState.Failed;
            Error = failure.Message;
        }
        catch (Exception unexpected)
        {
            // Anything reaching the panel as an unhandled exception would take
            // the whole application down with it. The user gets a message.
            State = TranslationState.Failed;
            Error = unexpected.Message;
        }

        Updated?.Invoke();
    }

    /// <summary>
    /// 换向的方向：旧目标成为声明的源语言，新目标取原文的本地先验——
    /// 先验与旧目标同名或不可用时退到 English，再同名退到 Chinese，
    /// 与面板手动换向的缺省一致。"中文原文配中文目标"这一最常见回声
    /// 因此恰好落进 Chinese→English 的有用方向。
    /// </summary>
    private static TranslationRequest EchoRetryDirection(TranslationRequest request)
    {
        var target = LanguageGuess.FromText(request.Text).LanguageName ?? "English";
        if (string.Equals(target, request.TargetLanguage, StringComparison.OrdinalIgnoreCase))
        {
            target = "English";
        }

        if (string.Equals(target, request.TargetLanguage, StringComparison.OrdinalIgnoreCase))
        {
            target = "Chinese";
        }

        return request with
        {
            TargetLanguage = target,
            SourceLanguage = request.TargetLanguage,
        };
    }
}

/// <summary>Adapts a backend that returns whole strings into the streaming shape.</summary>
public sealed class WholeTextBackend(Func<TranslationRequest, CancellationToken, Task<string>> translate)
    : ITranslationBackend
{
    public async IAsyncEnumerable<string> TranslateAsync(
        TranslationRequest request,
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        yield return await translate(request, cancellation);
    }
}
