namespace Shiyu.Core;

/// <summary>
/// The quick actions a hovered card can offer. Identified by stable ids
/// because the user's chosen set and order live in the settings file.
/// </summary>
public static class HoverActions
{
    /// <summary>Every action id that exists, in default order.</summary>
    public static readonly string[] All =
        ["copy", "paste", "plain", "open", "locate", "pin", "favorite", "note", "delete"];

    /// <summary>The id as the interface shows it.</summary>
    public static string Name(string id) => id switch
    {
        "copy" => "复制",
        "paste" => "粘贴",
        "plain" => "纯文本粘贴",
        "open" => "打开",
        "locate" => "定位",
        "pin" => "置顶",
        "favorite" => "收藏",
        "note" => "备注",
        "delete" => "删除",
        _ => id,
    };

    /// <summary>The glyph the tray button carries.</summary>
    public static string Glyph(string id) => id switch
    {
        "copy" => "复",
        "paste" => "贴",
        "plain" => "文",
        "open" => "开",
        "locate" => "位",
        "pin" => "钉",
        "favorite" => "★",
        "note" => "注",
        "delete" => "删",
        _ => "?",
    };

    /// <summary>Deleting cannot be undone, so it never looks like its neighbours.</summary>
    public static bool IsDestructive(string id) => id == "delete";

    /// <summary>
    /// Cleans a hand-edited choice: unknown ids dropped, duplicates dropped,
    /// the user's order kept. An unusable result falls back to the default
    /// rather than to an empty tray, because a tray with nothing in it reads
    /// as a broken feature.
    /// </summary>
    public static IReadOnlyList<string> Sanitise(IEnumerable<string>? chosen)
    {
        var seen = new HashSet<string>();
        var result = new List<string>();

        foreach (var id in chosen ?? [])
        {
            if (All.Contains(id) && seen.Add(id))
            {
                result.Add(id);
            }
        }

        return result.Count > 0 ? result : All;
    }

    /// <summary>
    /// Which of the chosen actions this entry can honour. The rest are hidden:
    /// a button that shows up only to report that it cannot work has already
    /// wasted the user's click.
    /// </summary>
    public static IReadOnlyList<string> AvailableFor(
        IEnumerable<string> chosen, EntryKind kind, bool hasOriginal)
        => chosen.Where(id => id switch
        {
            // Stored text is plain; pasting "as plain text" means something
            // only where formatted content could have been.
            "plain" => kind == EntryKind.Text,

            // Files carry their own paths to act on; images need the retained
            // original.
            "open" or "locate" => hasOriginal || kind == EntryKind.Files,
            _ => true,
        }).ToList();
}
