using System.Text.RegularExpressions;

namespace Shiyu.Core;

/// <summary>
/// Decides what never reaches the history.
///
/// The history is not encrypted, so this is the only thing standing between a
/// copied password and a plaintext row on disk that outlives the machine. It
/// therefore errs towards excluding: a rule that cannot be evaluated excludes
/// nothing (a broken rule must not blind the whole history), but anything an
/// application explicitly asked to be left alone is left alone.
/// </summary>
public sealed class ExclusionPolicy
{
    /// <summary>
    /// A user-written pattern runs against every copy, so a pathological one
    /// could otherwise stall the clipboard. Matching is abandoned instead.
    /// </summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);

    private readonly List<ExclusionRule> _rules;

    public ExclusionPolicy() => _rules = [];

    public ExclusionPolicy(IEnumerable<ExclusionRule> rules) => _rules = [.. rules];

    /// <summary>
    /// What Shiyu ships with, so a fresh install is safe before the user has
    /// configured anything.
    ///
    /// Only password managers are listed, and only by application name. Guessing
    /// at "this looks like a secret" from the text itself was considered and
    /// rejected: it is unreliable in both directions, and every false positive
    /// punches a silent hole in the history the user cannot see or explain.
    /// Payment and banking applications are covered by the marker instead —
    /// they set it, and their window titles are far too varied to enumerate.
    /// </summary>
    public static IReadOnlyList<ExclusionRule> Presets { get; } =
    [
        ExclusionRule.ForSourceApp("KeePass"),
        ExclusionRule.ForSourceApp("KeePassXC"),
        ExclusionRule.ForSourceApp("KeeWeb"),
        ExclusionRule.ForSourceApp("1Password"),
        ExclusionRule.ForSourceApp("Bitwarden"),
        ExclusionRule.ForSourceApp("LastPass"),
        ExclusionRule.ForSourceApp("Dashlane"),
        ExclusionRule.ForSourceApp("Enpass"),
        ExclusionRule.ForSourceApp("RoboForm"),
        ExclusionRule.ForSourceApp("NordPass"),
        ExclusionRule.ForSourceApp("ProtonPass"),
    ];

    public static ExclusionPolicy WithPresets() => new(Presets);

    public IReadOnlyList<ExclusionRule> Rules => _rules;

    public void Add(ExclusionRule rule)
    {
        if (!_rules.Contains(rule))
        {
            _rules.Add(rule);
        }
    }

    public void Remove(ExclusionRule rule) => _rules.Remove(rule);

    public bool Excludes(ClipboardSnapshot snapshot)
        => snapshot.ExcludedByMarker || _rules.Any(rule => Matches(rule, snapshot));

    private static bool Matches(ExclusionRule rule, ClipboardSnapshot snapshot) => rule.Kind switch
    {
        // Contains rather than equals: applications carry version numbers and
        // suffixes in their process names, and under-matching here is the
        // expensive direction.
        ExclusionRuleKind.SourceApp =>
            snapshot.SourceApp is not null
            && snapshot.SourceApp.Contains(rule.Value, StringComparison.OrdinalIgnoreCase),

        ExclusionRuleKind.ContentPattern => MatchesPattern(rule.Value, snapshot.Text),

        _ => false,
    };

    private static bool MatchesPattern(string pattern, string text)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase, MatchTimeout);
        }
        catch (ArgumentException)
        {
            // The user mistyped the pattern. Excluding nothing is the safe
            // reading: the alternative is a rule that quietly swallows
            // everything the user copies.
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
