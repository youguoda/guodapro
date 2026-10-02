namespace Shiyu.Core;

/// <summary>
/// The one gate every "read someone else's content" path passes (O-17).
/// ADR-0007 made the exclusion rules the only line of defence for the
/// history; this extends the same promise to the paths that never write a
/// row — capturing a selection by simulating Ctrl+C, and sending stored
/// entries to a model. The recording pipeline asks the policy directly with
/// a snapshot; capture paths cannot (they have a foreground application and
/// no content yet), and agent actions have stored entries, so the gate
/// offers both shapes.
/// </summary>
public static class CaptureGate
{
    /// <summary>
    /// Whether a capture must not happen: the foreground application is on
    /// the exclusion list. Marker and content rules do not apply here — at
    /// this point there is no content, and the marker defends copies, not
    /// keystrokes. Matching mirrors the policy's SourceApp rule (contains,
    /// case-insensitive): the same rule must not mean different things on
    /// different paths.
    /// </summary>
    public static bool BlocksForeground(ExclusionPolicy policy, string? foregroundApp)
        => foregroundApp is not null
           && policy.Rules.Any(rule =>
               rule.Kind == ExclusionRuleKind.SourceApp
               && foregroundApp.Contains(rule.Value, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Entries a model may see. Entries were judged when they were recorded,
    /// but the exclusion list may have grown since — "exclude the password
    /// manager" added last week must also stop last month's copied password
    /// from being summarised today.
    /// </summary>
    public static (IReadOnlyList<Entry> Sendable, int Skipped) SplitSendable(
        IEnumerable<Entry> entries, ExclusionPolicy policy)
    {
        var sendable = new List<Entry>();
        var skipped = 0;
        foreach (var entry in entries)
        {
            if (BlocksForeground(policy, entry.SourceApp))
            {
                skipped++;
            }
            else
            {
                sendable.Add(entry);
            }
        }

        return (sendable, skipped);
    }
}
