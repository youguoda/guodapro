using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// The value half of the settings tree: what each id reads from and writes to
/// on <see cref="AppSettings"/>. The renderer never knows a setting exists;
/// adding one is a schema leaf plus a case here, and both are data-shaped.
/// </summary>
internal static class SettingsBindings
{
    private const string PatternPrefix = "re:";

    /// <summary>An item's current value as editor text; null for "nothing to show".</summary>
    public static string? ReadText(string id, AppSettings settings) => id switch
    {
        "exclusions" => string.Join(
            Environment.NewLine,
            settings.ExclusionRules.Select(rule => rule.Kind == ExclusionRuleKind.ContentPattern
                ? PatternPrefix + rule.Value
                : rule.Value)),
        "bar.actions" => string.Join(",", settings.BarActions.Select(HoverActions.Name)),
        "theme" => null,
        "action.sound" => null,
        "store.protect" => null,
        "store.protect-favorites" => null,
        "store.protect-pinned" => null,
        "store.start-with-windows" => null,
        "about.update-auto" => null,
        "store.retention-days" => settings.ImageRetentionDays.ToString(),
        "bar.text-lines" => settings.BarTextLines.ToString(),
        "bar.image-height" => settings.BarImageHeight.ToString(),
        "bar.file-count" => settings.BarFileCount.ToString(),
        "look.preview-hover" => settings.PreviewHoverDelayMs.ToString(),
        "hotkey.capture" => settings.CaptureHotkey,
        "hotkey.clipboard" => settings.ClipboardTranslateHotkey,
        "hotkey.quickbar" => settings.QuickBarHotkey,
        "hotkey.bar" => settings.BarHotkey,
        "service.target-language" => settings.TargetLanguage,
        "service.source-language" => settings.SourceLanguage ?? string.Empty,
        "service.base-url" => settings.BackendBaseUrl,
        "service.model" => settings.BackendModel,

        // The stored credential is never put back into an editor.
        "service.api-key" => string.Empty,
        "store.directory" => settings.DataDirectoryOverride,
        "about.version" => VersionText,
        _ => null,
    };

    /// <summary>The index a segmented control rests on, for choice-shaped items.</summary>
    public static int ReadChoice(string id, AppSettings settings) => id switch
    {
        "theme" => (int)settings.Theme,
        "service.backend-kind" => (int)settings.TranslationBackend,
        _ => 0,
    };

    /// <summary>Whether a toggle item rests on. Null for not-a-toggle.</summary>
    public static bool? ReadToggle(string id, AppSettings settings) => id switch
    {
        "action.sound" => settings.ActionSound,
        "look.lightweight" => settings.LightweightWhenHidden,
        "bar.at-cursor" => settings.BarAtCursor,
        "look.bar-topmost" => settings.BarAlwaysOnTop,
        "record.images" => settings.RecordImages,
        "record.files" => settings.RecordFiles,
        "winv.takeover" => settings.TakeOverWinV,
        "hotkeys.selection-badge" => settings.SelectionBadge,
        "store.protect" => settings.ProtectEntries,
        "store.protect-favorites" => settings.ProtectFavorites,
        "store.protect-pinned" => settings.ProtectPinned,
        "about.update-auto" => settings.UpdateAutoCheck,
        _ => null,
    };

    /// <summary>
    /// Writes one edited value into a growing settings copy. Text is what
    /// editors naturally hold; each case turns it into its typed shape.
    /// </summary>
    public static AppSettings Apply(string id, AppSettings current, string text, int choice) => id switch
    {
        "exclusions" => current with { ExclusionRules = ParseExclusionRules(text) },
        "bar.actions" => current,
        "theme" => current with { Theme = (AppTheme)choice },
        "service.backend-kind" => current with { TranslationBackend = (TranslationBackendKind)choice },
        "action.sound" => current with { ActionSound = AsBool(text) },
        "bar.at-cursor" => current with { BarAtCursor = AsBool(text) },
        "look.bar-topmost" => current with { BarAlwaysOnTop = AsBool(text) },
        "look.lightweight" => current with { LightweightWhenHidden = AsBool(text) },
        "record.images" => current with { RecordImages = AsBool(text) },
        "record.files" => current with { RecordFiles = AsBool(text) },
        "winv.takeover" => current with { TakeOverWinV = AsBool(text) },
        "hotkeys.selection-badge" => current with { SelectionBadge = AsBool(text) },
        "store.protect" => current with { ProtectEntries = AsBool(text) },
        "store.protect-favorites" => current with { ProtectFavorites = AsBool(text) },
        "store.protect-pinned" => current with { ProtectPinned = AsBool(text) },
        "about.update-auto" => current with { UpdateAutoCheck = AsBool(text) },
        "store.start-with-windows" => current with { StartWithWindows = AsBool(text) },
        "store.retention-days" => current with { ImageRetentionDays = int.Parse(text) },
        "bar.text-lines" => current with { BarTextLines = int.Parse(text) },
        "bar.image-height" => current with { BarImageHeight = int.Parse(text) },
        "bar.file-count" => current with { BarFileCount = int.Parse(text) },
        "look.preview-hover" => current with { PreviewHoverDelayMs = int.Parse(text) },
        "hotkey.capture" => current with { CaptureHotkey = text },
        "hotkey.clipboard" => current with { ClipboardTranslateHotkey = text },
        "hotkey.quickbar" => current with { QuickBarHotkey = text },
        "hotkey.bar" => current with { BarHotkey = text },
        "service.target-language" => current with { TargetLanguage = text },
        "service.source-language" => current with
        {
            SourceLanguage = text.Trim() is { Length: > 0 } source ? source : null,
        },
        "service.base-url" => current with { BackendBaseUrl = text },
        "service.model" => current with { BackendModel = text },

        // Blank means keep: the user should not have to retype a secret to
        // change an unrelated setting.
        "service.api-key" => current with
        {
            BackendApiKey = text.Length > 0 ? text : current.BackendApiKey,
        },
        "store.directory" => current with { DataDirectoryOverride = text.Trim() },
        _ => current,
    };

    private static bool AsBool(string text) => text == "1";

    // --- 只写改过的项（O-20） ---------------------------------------------------
    //
    // 设置窗和引导窗都攥着一份开窗快照：保存时若把所有项重放一遍，别处
    // 在此期间改过的字段就会被旧值覆盖（S1–S4 的共同根源）。以下两个
    // 成员把"哪些项真的改了"变成数据：对照快照逐项比较，未变的项不动。

    /// <summary>
    /// 一项的编辑值是否仍与快照一致。一致的项在保存时不写——让别处的
    /// 修改活下来。
    /// </summary>
    public static bool IsUnchanged(string id, AppSettings baseline, ItemState state) => id switch
    {
        "theme" => state.Choice == (int)baseline.Theme,
        "service.backend-kind" => state.Choice == (int)baseline.TranslationBackend,

        // 这个开关的基线是 Windows 的现状而非文件值（两者可能不一致，
        // 而 Windows 是事实）。既有保存行为是每次都照 Windows 现状写回
        // 文件——保持不变，别让"没碰过的开关"在保存后反向改写注册表。
        "store.start-with-windows" => false,

        _ when ReadToggle(id, baseline) is { } on => state.Text == (on ? "1" : "0"),
        _ => ReadText(id, baseline) is { } text
            && string.Equals(text.Trim(), state.Text.Trim(), StringComparison.Ordinal),
    };

    /// <summary>
    /// 把编辑器状态变成"在最新设置上只应用改过项"的增量函数。
    /// </summary>
    public static Func<AppSettings, AppSettings> ChangedOnly(
        AppSettings baseline, IReadOnlyDictionary<string, ItemState> states)
        => current =>
        {
            var updated = current;
            foreach (var (id, state) in states)
            {
                if (IsUnchanged(id, baseline, state))
                {
                    continue;
                }

                updated = Apply(id, updated, state.Text, state.Choice);
            }

            return updated;
        };

    internal static List<StoredExclusionRule> ParseExclusionRules(string text)
        => text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.StartsWith(PatternPrefix, StringComparison.OrdinalIgnoreCase)
                ? new StoredExclusionRule(ExclusionRuleKind.ContentPattern, line[PatternPrefix.Length..].Trim())
                : new StoredExclusionRule(ExclusionRuleKind.SourceApp, line))
            .Where(rule => rule.Value.Length > 0)
            .ToList();

    public static string VersionText
        => "v" + (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0");
}
