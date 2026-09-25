namespace Shiyu.Core;

/// <summary>
/// What the user is narrowing the history down to. Every part is optional and
/// they combine: the point of filters here is finding "that image from last
/// week", which needs two of them at once.
/// </summary>
public sealed record HistoryFilter
{
    public static HistoryFilter None { get; } = new();

    /// <summary>Substring match against the text. Blank means no keyword.</summary>
    public string? Query { get; init; }

    /// <summary>Inclusive lower bound on when the entry was created.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Inclusive upper bound.</summary>
    public DateTimeOffset? To { get; init; }

    public EntryKind? Kind { get; init; }

    /// <summary>Only entries of this shape — links, colours, paths. Null means no subtype filter.</summary>
    public EntrySubtype? Subtype { get; init; }

    /// <summary>True narrows to favourites; false and null both mean no favourite filter.</summary>
    public bool? Favorite { get; init; }

    /// <summary>Only entries carrying this tag. Blank means no tag filter.</summary>
    public string? Tag { get; init; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Query) && From is null && To is null && Kind is null
        && Subtype is null && Favorite != true && string.IsNullOrWhiteSpace(Tag);
}
