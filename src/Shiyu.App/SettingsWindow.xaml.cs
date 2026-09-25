using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>
/// Everything the user can change, on one screen.
///
/// Deliberately short — light/dark is as far as appearance goes; no skins, no
/// advanced panels. The fewer settings there are, the more "small and well
/// made" is something the application can actually claim.
/// </summary>
public partial class SettingsWindow : Window
{
    private const string PatternPrefix = "re:";

    private readonly AppSettings _current;
    private readonly Action<AppSettings> _apply;
    private readonly BackupUi? _backup;

    public SettingsWindow(AppSettings current, Action<AppSettings> apply, BackupUi? backup = null)
    {
        InitializeComponent();

        _current = current;
        _apply = apply;
        _backup = backup;

        // ComboBox order matches the AppTheme enum: System, Light, Dark.
        ThemeChoice.SelectedIndex = (int)current.Theme;

        CaptureHotkey.Text = current.CaptureHotkey;
        ClipboardHotkey.Text = current.ClipboardTranslateHotkey;
        QuickBarHotkey.Text = current.QuickBarHotkey;
        BarHotkey.Text = current.BarHotkey;

        BarTextLines.Text = current.BarTextLines.ToString();
        BarImageHeight.Text = current.BarImageHeight.ToString();
        BarFileCount.Text = current.BarFileCount.ToString();

        BarActions.Text = string.Join(",", current.BarActions.Select(HoverActions.Name));
        ActionSound.IsChecked = current.ActionSound;

        TargetLanguage.Text = current.TargetLanguage;
        SourceLanguage.Text = current.SourceLanguage ?? string.Empty;

        BackendUrl.Text = current.BackendBaseUrl;
        BackendModel.Text = current.BackendModel;

        // The stored credential is never put back into a box where it could be
        // read over a shoulder or copied out. Leaving it blank keeps it.
        KeyHint.Text = current.BackendApiKey.Length > 0
            ? "已保存凭据。留空表示不改动，填入则覆盖。"
            : "尚未填写凭据，翻译功能需要它才能工作。";

        RetentionDays.Text = current.ImageRetentionDays.ToString();
        ProtectFavorites.IsChecked = current.ProtectFavorites;
        ProtectPinned.IsChecked = current.ProtectPinned;
        DataDirectory.Text = current.DataDirectoryOverride;

        ExclusionRules.Text = string.Join(
            Environment.NewLine,
            current.ExclusionRules.Select(rule => rule.Kind == ExclusionRuleKind.ContentPattern
                ? PatternPrefix + rule.Value
                : rule.Value));

        PresetNote.Text =
            $"另有 {ExclusionPolicy.Presets.Count} 条内置规则（常见密码管理器）始终生效，无需在此重复填写。";

        // Read from Windows rather than from the settings file: the two can
        // disagree, and what Windows actually does is the truth.
        StartWithWindows.IsChecked = StartupRegistration.IsEnabled();

        UpdateSyncWarning();
    }

    private void OnDataDirectoryChanged(object sender, TextChangedEventArgs e) => UpdateSyncWarning();

    private void UpdateSyncWarning()
    {
        var folder = CloudSyncedPaths.DetectSyncFolder(DataDirectory.Text);

        SyncWarning.Visibility = folder is null ? Visibility.Collapsed : Visibility.Visible;
        SyncWarning.Text = folder is null
            ? string.Empty
            : $"⚠ 这个位置在「{folder}」里，会被同步到云端。历史记录并未加密，"
              + "放在这里等于把明文的剪贴板内容交给同步服务。";
    }

    private void OnBrowseDataDirectory(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择拾语存放数据的位置",
            InitialDirectory = Directory.Exists(DataDirectory.Text)
                ? DataDirectory.Text
                : AppPaths.DataDirectory,
        };

        if (dialog.ShowDialog(this) == true)
        {
            DataDirectory.Text = dialog.FolderName;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var problems = new List<string>();

        var capture = RequireHotkey(CaptureHotkey.Text, "划词翻译", problems);
        var clipboard = RequireHotkey(ClipboardHotkey.Text, "翻译剪贴板", problems);
        var quickBar = RequireHotkey(QuickBarHotkey.Text, "快速条", problems);
        var bar = RequireHotkey(BarHotkey.Text, "窄条", problems);

        if (!int.TryParse(RetentionDays.Text, out var retention) || retention < 1)
        {
            problems.Add("图片保留天数需要是一个不小于 1 的整数。");
        }

        // Density knobs: wide enough ranges that every screen and taste fits,
        // narrow enough that nothing pathological does.
        if (!int.TryParse(BarTextLines.Text, out var textLines) || textLines is < 1 or > 20)
        {
            problems.Add("窄条文本行数需要在 1 到 20 之间。");
        }

        if (!int.TryParse(BarImageHeight.Text, out var imageHeight) || imageHeight is < 40 or > 400)
        {
            problems.Add("窄条图片高度需要在 40 到 400 之间。");
        }

        if (!int.TryParse(BarFileCount.Text, out var fileCount) || fileCount is < 1 or > 10)
        {
            problems.Add("窄条文件条数需要在 1 到 10 之间。");
        }

        var actions = ParseBarActions(problems);

        if (TargetLanguage.Text.Trim().Length == 0)
        {
            problems.Add("译文语言不能为空。");
        }

        var chosen = new[] { capture, clipboard, quickBar, bar }.Where(h => h is not null).ToList();
        if (chosen.Count == 4 && chosen.Distinct().Count() != 4)
        {
            // Registering the same combination twice means the second one
            // silently never works.
            problems.Add("四个快捷键不能相同。");
        }

        if (problems.Count > 0)
        {
            SaveStatus.Text = string.Join(" ", problems);
            return;
        }

        var directory = DataDirectory.Text.Trim();
        if (CloudSyncedPaths.DetectSyncFolder(directory) is { } folder
            && !Confirm($"「{folder}」会被同步到云端，而历史记录并未加密。确定要把数据放在这里吗？"))
        {
            return;
        }

        var updated = _current with
        {
            CaptureHotkey = capture!.ToString(),
            ClipboardTranslateHotkey = clipboard!.ToString(),
            QuickBarHotkey = quickBar!.ToString(),
            BarHotkey = bar!.ToString(),
            BarTextLines = textLines,
            BarImageHeight = imageHeight,
            BarFileCount = fileCount,
            BarActions = actions,
            ActionSound = ActionSound.IsChecked == true,
            TargetLanguage = TargetLanguage.Text.Trim(),
            SourceLanguage = SourceLanguage.Text.Trim() is { Length: > 0 } source ? source : null,
            BackendBaseUrl = BackendUrl.Text.Trim(),
            BackendModel = BackendModel.Text.Trim(),

            // Blank means "leave it as it was", so the user is not forced to
            // retype a credential to change an unrelated setting.
            BackendApiKey = BackendKey.Password.Length > 0 ? BackendKey.Password : _current.BackendApiKey,
            ImageRetentionDays = retention,
            ProtectFavorites = ProtectFavorites.IsChecked == true,
            ProtectPinned = ProtectPinned.IsChecked == true,
            DataDirectoryOverride = directory,
            StartWithWindows = StartWithWindows.IsChecked == true,
            Theme = (AppTheme)ThemeChoice.SelectedIndex,
            ExclusionRules = ParseExclusionRules(ExclusionRules.Text),
        };

        var startupOk = StartupRegistration.Set(
            updated.StartWithWindows, Environment.ProcessPath ?? string.Empty);

        _apply(updated);

        SaveStatus.Text = startupOk
            ? "已保存。"
            : "设置已保存，但开机自启没能写入系统，请检查是否有安全软件拦截。";

        // Follow whatever Windows ended up doing rather than leaving the box
        // asserting something untrue.
        StartWithWindows.IsChecked = StartupRegistration.IsEnabled();
        BackendKey.Clear();
    }

    private static HotkeySpec? RequireHotkey(string text, string name, List<string> problems)
    {
        var parsed = HotkeySpec.Parse(text);
        if (parsed is null)
        {
            problems.Add($"{name}的快捷键无法识别，需要形如 Ctrl+Shift+Z 且至少带一个修饰键。");
        }

        return parsed;
    }

    private static List<StoredExclusionRule> ParseExclusionRules(string text)
        => text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.StartsWith(PatternPrefix, StringComparison.OrdinalIgnoreCase)
                ? new StoredExclusionRule(ExclusionRuleKind.ContentPattern, line[PatternPrefix.Length..].Trim())
                : new StoredExclusionRule(ExclusionRuleKind.SourceApp, line))
            .Where(rule => rule.Value.Length > 0)
            .ToList();

    private bool Confirm(string message)
        => MessageBox.Show(
            this, message, "拾语", MessageBoxButton.OKCancel,
            MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;

    /// <summary>
    /// Parses the comma-separated action list the user typed. Names rather
    /// than ids, because ids are for files and names are for people; anything
    /// unrecognised is a problem rather than a silent drop, because a
    /// silently-shrinking tray looks like a bug.
    /// </summary>
    private List<string> ParseBarActions(List<string> problems)
    {
        var byName = HoverActions.All.ToDictionary(HoverActions.Name, StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var raw in BarActions.Text.Split([',', '，', '、'], StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0)
            {
                continue;
            }

            if (byName.TryGetValue(raw, out var id))
            {
                if (!result.Contains(id))
                {
                    result.Add(id);
                }
            }
            else
            {
                problems.Add($"悬停动作「{raw}」无法识别。");
            }
        }

        if (result.Count == 0)
        {
            problems.Add("至少需要一个悬停动作。");
        }

        return result;
    }

    private void OnExportBackup(object sender, RoutedEventArgs e)
        => _backup?.Export(this);

    private void OnImportBackup(object sender, RoutedEventArgs e)
        => _backup?.Import(this);

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
