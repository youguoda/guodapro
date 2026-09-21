namespace Shiyu.Core;

/// <summary>
/// One record in the clipboard history. Every copy produces an entry —
/// filtering decides whether the badge appears, never whether we record.
/// </summary>
public sealed record Entry(long Id, string Text, string? SourceApp, DateTimeOffset CreatedAt);
