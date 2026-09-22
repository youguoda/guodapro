namespace Shiyu.Core;

public sealed record TranslationRequest(string Text, string TargetLanguage)
{
    /// <summary>Null asks the backend to work it out from the text.</summary>
    public string? SourceLanguage { get; init; }
}

/// <summary>
/// A translation backend.
///
/// Streaming from the very first version, deliberately: retrofitting a stream
/// into a blocking interface means changing every caller, every view and every
/// test at once. The user should be reading while the rest is still arriving.
/// </summary>
public interface ITranslationBackend
{
    /// <summary>
    /// Yields the translation in pieces as they arrive. Implementations throw
    /// <see cref="TranslationFailedException"/> for anything the user needs to
    /// be told about.
    /// </summary>
    IAsyncEnumerable<string> TranslateAsync(TranslationRequest request, CancellationToken cancellation);
}

public sealed class TranslationFailedException(string message, Exception? inner = null)
    : Exception(message, inner);
