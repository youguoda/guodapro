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

    public bool StartWithWindows { get; init; } = true;

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
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Format) ?? new AppSettings()
                : new AppSettings();
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
