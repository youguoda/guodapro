namespace Shiyu.Core;

public enum ExclusionRuleKind
{
    /// <summary>Matches on the application the content was copied from.</summary>
    SourceApp,

    /// <summary>Matches on the copied text itself, as a regular expression.</summary>
    ContentPattern,
}

/// <summary>One user-configurable reason to keep something out of the history.</summary>
public sealed record ExclusionRule(ExclusionRuleKind Kind, string Value)
{
    public static ExclusionRule ForSourceApp(string name) => new(ExclusionRuleKind.SourceApp, name);

    public static ExclusionRule ForContentPattern(string pattern)
        => new(ExclusionRuleKind.ContentPattern, pattern);
}
