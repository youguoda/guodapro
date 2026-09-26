using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shiyu.Core;

/// <summary>
/// Everything the user can change.
///
/// Deliberately small. A settings screen with thirty knobs on it is the
/// opposite of the tool this is meant to be; each new entry here should have to
/// argue for itself.
/// </summary>
public sealed record AppSettings
{
    /// <summary>What translations are produced in.</summary>
    public string TargetLanguage { get; init; } = "Chinese";

    /// <summary>Null lets the backend work it out from the text.</summary>
    public string? SourceLanguage { get; init; }

    public string BackendBaseUrl { get; init; } = string.Empty;

    public string BackendModel { get; init; } = string.Empty;

    /// <summary>
    /// Stored as written. The history beside it is not encrypted either, so
    /// pretending this one field is protected would be theatre — what it does
    /// get is never being shown in the interface or written to a log.
    /// </summary>
    public string BackendApiKey { get; init; } = string.Empty;

    /// <summary>How long image originals are kept before being cleaned up.</summary>
    public int ImageRetentionDays { get; init; } = 30;

    /// <summary>
    /// The master switch of delete protection — children name who is spared.
    /// Off means protection is entirely off, whichever child stays checked.
    /// </summary>
    public bool ProtectEntries { get; init; } = true;

    /// <summary>
    /// Favourites survive retention sweeps and bulk deletes. On by default:
    /// a starred entry is a promise the user made to themselves, and the
    /// delete entry points for it disappear rather than grey out.
    /// </summary>
    public bool ProtectFavorites { get; init; } = true;

    /// <summary>Pins survive retention sweeps and bulk deletes, like favourites.</summary>
    public bool ProtectPinned { get; init; } = true;

    /// <summary>
    /// When the bar hides, drop its realised cards and trim the process —
    /// a resident tray tool should cost pennies while idle. Recording never
    /// pauses: the listener and the pipeline do not live in the window.
    /// </summary>
    public bool LightweightWhenHidden { get; init; } = true;

    /// <summary>Whether copied images are recorded. Text always is — without it there is no tool.</summary>
    public bool RecordImages { get; init; } = true;

    /// <summary>
    /// Summon the bar beside the cursor, like the system's Win+V panel, rather
    /// than at a fixed remembered spot. On by default: near where you are
    /// typing is where you are about to paste. Off restores the resident
    /// window's remembered geometry.
    /// </summary>
    public bool BarAtCursor { get; init; } = true;

    /// <summary>Whether copied file lists are recorded.</summary>
    public bool RecordFiles { get; init; } = true;

    /// <summary>Set once the first-run guide has run or been skipped; it never returns on its own.</summary>
    public bool OnboardingCompleted { get; init; }

    /// <summary>
    /// Take Win+V away from the system clipboard panel. Off by default: a
    /// system key is being borrowed, so only the user's explicit choice does
    /// it — and the hook lives in-process, so closing or killing Shiyu hands
    /// the key back to Windows by itself.
    /// </summary>
    public bool TakeOverWinV { get; init; }

    /// <summary>
    /// A tray tool nobody starts is a tray tool nobody has; on by default,
    /// and the user can switch it off.
    /// </summary>
    public bool StartWithWindows { get; init; } = true;

    /// <summary>Colours follow Windows when this is System; changing it applies live.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
    public AppTheme Theme { get; init; } = AppTheme.System;

    public string CaptureHotkey { get; init; } = "Ctrl+Shift+Z";

    public string ClipboardTranslateHotkey { get; init; } = "Ctrl+Shift+X";

    public string QuickBarHotkey { get; init; } = "Ctrl+Shift+V";

    /// <summary>Summons and hides the resident narrow bar.</summary>
    public string BarHotkey { get; init; } = "Ctrl+Shift+B";

    // --- narrow bar density ---
    // Density is how much content each card clamps, never how small the text
    // gets: sizes and paddings stay fixed so the list can never look cramped
    // or empty, only show more or less of each entry.

    /// <summary>How many lines of text a card shows before clamping.</summary>
    public int BarTextLines { get; init; } = 4;

    /// <summary>The tallest an image card may be, in device-independent units.</summary>
    public int BarImageHeight { get; init; } = 120;

    /// <summary>How many files a file card lists before clamping. Reserved until file entries exist.</summary>
    public int BarFileCount { get; init; } = 3;

    /// <summary>
    /// Which hover actions a card offers, in the user's chosen order, as ids
    /// from <see cref="HoverActions"/>. Sanitised on use, so a hand-edited
    /// file degrades to fewer buttons rather than to a broken tray.
    /// </summary>
    public IReadOnlyList<string> BarActions { get; init; } = HoverActions.All;

    /// <summary>
    /// Whether completed actions also play a sound. Off by default: the
    /// one-second tick on the button is the feedback; a sound on every copy
    /// is a toy piano.
    /// </summary>
    public bool ActionSound { get; init; }

    // --- narrow bar geometry, remembered between sessions ---
    // Null means "never placed yet"; the width is fixed by design and not stored.

    public double? BarLeft { get; init; }

    public double? BarTop { get; init; }

    public double? BarHeight { get; init; }

    /// <summary>
    /// Blank means the default beside the application data. Changing it needs a
    /// restart, and a synced folder needs a warning first — the history is not
    /// encrypted, so syncing it puts plaintext on someone else's servers.
    /// </summary>
    public string DataDirectoryOverride { get; init; } = string.Empty;

    /// <summary>Source-app and content-pattern rules the user added themselves.</summary>
    public IReadOnlyList<StoredExclusionRule> ExclusionRules { get; init; } = [];

    [JsonIgnore]
    public TranslationBackendOptions Backend => new(BackendBaseUrl, BackendModel, BackendApiKey);

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Reads the settings, falling back to defaults for anything missing or
    /// unreadable. A corrupt settings file must not stop Shiyu from starting:
    /// with no window to show an error in, that would look like a tool that
    /// simply died.
    /// </summary>
    public static AppSettings Load(string path)
    {
        try
        {
            var loaded = File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Format) ?? new AppSettings()
                : new AppSettings();

            // A saved action list that exactly matches a former default is a
            // default that predates newer actions, not a choice — upgrade it,
            // while respecting anything the user actually reordered or pruned.
            if (loaded.BarActions.SequenceEqual(FormerDefaultActions)
                || loaded.BarActions.SequenceEqual(HoverActions.FormerDefaultWithoutGroup))
            {
                loaded = loaded with { BarActions = HoverActions.All };
            }

            return loaded;
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>The default action list before favourites and notes existed.</summary>
    private static readonly string[] FormerDefaultActions =
        ["copy", "paste", "plain", "open", "locate", "pin", "delete"];

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Written beside the target and moved into place, so an interrupted
        // save leaves the previous settings rather than half a file.
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, Format));
        File.Move(temporary, path, overwrite: true);
    }

    public ExclusionPolicy BuildExclusionPolicy()
        => new(ExclusionPolicy.Presets.Concat(
            ExclusionRules.Select(rule => new ExclusionRule(rule.Kind, rule.Value))));
}

/// <param name="Kind">Whether the rule matches the source application or the text.</param>
public sealed record StoredExclusionRule(ExclusionRuleKind Kind, string Value);
