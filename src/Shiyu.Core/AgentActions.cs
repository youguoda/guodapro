using System.Runtime.CompilerServices;
using System.Text;

namespace Shiyu.Core;

/// <param name="SystemPrompt">How the model should behave.</param>
/// <param name="UserContent">The material it works on. Nothing else is sent.</param>
public sealed record ModelRequest(string SystemPrompt, string UserContent)
{
    /// <summary>
    /// 采样温度。翻译走低温默认（<see cref="TranslationPrompt.DefaultTemperature"/>），
    /// 需要发散的调用方从这里覆盖。
    /// </summary>
    public double Temperature { get; init; } = TranslationPrompt.DefaultTemperature;
}

/// <summary>
/// A streaming text model. The same transport the translation panel uses —
/// actions reuse it rather than opening a second way to talk to a model.
/// </summary>
public interface IStreamingModel
{
    IAsyncEnumerable<string> StreamAsync(ModelRequest request, CancellationToken cancellation);
}

public enum AgentActionKind
{
    /// <summary>Condense the selection into a few lines.</summary>
    Summarise,

    /// <summary>Weave several entries into one coherent note.</summary>
    MergeIntoNote,

    /// <summary>Tidy the wording without changing what it says.</summary>
    Rewrite,

    /// <summary>Propose labels the user can accept or discard.</summary>
    SuggestTags,
}

public static class AgentActions
{
    public static string Label(AgentActionKind kind) => kind switch
    {
        AgentActionKind.Summarise => "总结",
        AgentActionKind.MergeIntoNote => "合并成笔记",
        AgentActionKind.Rewrite => "改写",
        AgentActionKind.SuggestTags => "建议标签",
        _ => kind.ToString(),
    };

    public static string SystemPrompt(AgentActionKind kind) => kind switch
    {
        AgentActionKind.Summarise =>
            """
            Summarise the clipboard entries the user gives you, in the language they are written in.
            Be brief and concrete. Output the summary only — no preamble, no closing remarks.
            """,

        AgentActionKind.MergeIntoNote =>
            """
            Weave the clipboard entries the user gives you into one coherent note, in the language
            they are written in. Keep every fact; drop repetition. Use headings or bullets only where
            they genuinely help. Output the note only — no preamble, no closing remarks.
            """,

        AgentActionKind.Rewrite =>
            """
            Rewrite the clipboard entries the user gives you so they read clearly and naturally, in
            the language they are written in. Do not add information and do not remove any.
            Output the rewritten text only — no preamble, no explanation of what you changed.
            """,

        AgentActionKind.SuggestTags =>
            """
            Suggest between two and five short labels for the clipboard entries the user gives you,
            in the language they are written in. Prefer labels that would still make sense months
            later. Output the labels only, one per line, with no numbering, punctuation or commentary.
            """,

        _ => "Respond helpfully to the clipboard entries the user gives you.",
    };

    /// <summary>
    /// Assembles what gets sent.
    ///
    /// The single most important property of this whole feature: the result
    /// contains the given entries and nothing else. No surrounding history, no
    /// neighbouring entries, nothing the user did not select.
    /// </summary>
    public static string BuildContent(IEnumerable<Entry> selected)
    {
        var builder = new StringBuilder();
        var index = 0;

        foreach (var entry in selected)
        {
            if (index > 0)
            {
                builder.AppendLine();
            }

            builder.Append("--- ").Append(++index).AppendLine(" ---");
            builder.AppendLine(entry.Text);
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Reads suggested labels back out of the model's answer, so the user can
    /// accept them individually rather than retyping.
    /// </summary>
    public static IReadOnlyList<string> ParseSuggestedTags(string output)
        => output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)

            // Models reach for bullets and numbering however firmly they are
            // asked not to; stripping them is cheaper than being strict.
            .Select(line => line.TrimStart('-', '*', '•', ' ', '\t'))
            .Select(line => line.TrimStart("0123456789.、) ".ToCharArray()))
            .Where(line => line.Length is > 0 and <= 24)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
}

/// <summary>
/// Runs one action over a selection and accumulates the streamed answer.
///
/// Every run starts from something the user selected and pressed a button on.
/// There is no path through this class that fires on its own.
/// </summary>
public sealed class AgentRun(IStreamingModel model)
{
    private readonly List<string> _pieces = [];

    public TranslationState State { get; private set; } = TranslationState.Idle;

    public string? Error { get; private set; }

    public string Output => string.Concat(_pieces).Trim();

    public event Action? Updated;

    /// <summary>What was sent, kept so a test can prove nothing extra went out.</summary>
    public ModelRequest? LastRequest { get; private set; }

    public async Task RunAsync(
        AgentActionKind kind,
        IReadOnlyList<Entry> selected,
        CancellationToken cancellation = default)
    {
        _pieces.Clear();
        Error = null;

        if (selected.Count == 0)
        {
            // Nothing selected means nothing to send — and nothing is sent.
            State = TranslationState.Finished;
            Updated?.Invoke();
            return;
        }

        var request = new ModelRequest(
            AgentActions.SystemPrompt(kind),
            AgentActions.BuildContent(selected));

        LastRequest = request;
        State = TranslationState.Streaming;
        Updated?.Invoke();

        try
        {
            await foreach (var piece in model.StreamAsync(request, cancellation))
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
            // An unreachable agent must leave the rest of Shiyu working.
            State = TranslationState.Failed;
            Error = unexpected.Message;
        }

        Updated?.Invoke();
    }
}

/// <summary>Adapts the translation backend's transport to the general shape.</summary>
public sealed class StreamingModelAdapter(
    Func<ModelRequest, CancellationToken, IAsyncEnumerable<string>> stream) : IStreamingModel
{
    public IAsyncEnumerable<string> StreamAsync(ModelRequest request, CancellationToken cancellation)
        => stream(request, cancellation);
}
