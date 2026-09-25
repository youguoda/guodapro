namespace Shiyu.Core;

public enum EntryKind
{
    Text,
    Image,
}

/// <summary>
/// One record in the clipboard history. Every copy produces an entry —
/// filtering decides whether the badge appears, never whether we record.
/// </summary>
/// <param name="Text">For an image entry, a short human-readable stand-in.</param>
/// <param name="ThumbnailPng">
/// Kept forever, unlike the original. That is what stops the history developing
/// holes the user cannot explain once old images are cleaned up.
/// </param>
/// <param name="OriginalPath">
/// Where the full-size image lives, until retention removes it. Null once it is
/// gone — the entry and its thumbnail stay.
/// </param>
public sealed record Entry(
    long Id,
    string Text,
    string? SourceApp,
    DateTimeOffset CreatedAt)
{
    public EntryKind Kind { get; init; } = EntryKind.Text;

    /// <summary>What a text entry is — a link, an email, a colour, a path — so the list can show it as itself.</summary>
    public EntrySubtype Subtype { get; init; } = EntrySubtype.None;

    public byte[]? ThumbnailPng { get; init; }

    public string? OriginalPath { get; init; }

    /// <summary>Pinned entries sort ahead of everything else.</summary>
    public bool IsPinned { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>False once retention has removed the original from disk.</summary>
    public bool HasOriginal => OriginalPath is { Length: > 0 } path && File.Exists(path);
}

/// <summary>An entry not yet written down, used for bulk writes.</summary>
public sealed record NewEntry(string Text, string? SourceApp, DateTimeOffset CreatedAt);
