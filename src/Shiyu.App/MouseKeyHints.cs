using System.IO;
using System.Text.Json;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// L2 适时教学（§5.2、票 25）：同一个有快捷键的动作被<b>鼠标</b>触发到第
/// 3 次时，一次性提示「下次可以直接按 D」——每个动作一生只提示一次。计
/// 数只记鼠标路径（键盘路径本身就是答案），进度存在 settings.json 旁的
/// keymap-hints.json：坏文件读不了就当作全都没提示过，这严格好过挡住
/// 动作；提示永远不是关键路径。
///
/// 提示里的键从 <see cref="KeyMap"/> 取——改表即改提示，与键帽同源。
/// </summary>
internal static class MouseKeyHints
{
    /// <summary>第几次鼠标触发时提示：一次是偶遇，两次是习惯，三次该认识键了。</summary>
    private const int TriggerAt = 3;

    private sealed record State(Dictionary<string, int> MouseUses, List<string> Shown);

    private static string FilePath => Path.Combine(AppPaths.DataDirectory, "keymap-hints.json");

    private static Dictionary<string, int>? _mouseUses;
    private static HashSet<string>? _shown;
    private static bool _loaded;

    /// <summary>
    /// Records one mouse-triggered action. Answers the one-off hint sentence
    /// when this is the trigger count and the action was never hinted before,
    /// null otherwise（含没有快捷键的动作）。
    /// </summary>
    public static string? Note(string actionId)
    {
        Load();

        // 没有键的动作无从提示——粘贴走 Enter、纯文本/定位本就无键。
        if (KeyMap.TrayKey(actionId) is not { Length: > 0 } key)
        {
            return null;
        }

        if (_shown!.Contains(actionId))
        {
            return null;
        }

        _mouseUses![actionId] = _mouseUses.TryGetValue(actionId, out var uses) ? uses + 1 : 1;
        if (_mouseUses[actionId] != TriggerAt)
        {
            Save();
            return null;
        }

        _shown.Add(actionId);
        Save();
        return $"下次可以直接按 {key}";
    }

    private static void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _mouseUses = [];
        _shown = [];
        try
        {
            if (File.Exists(FilePath)
                && JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) is { } state)
            {
                _mouseUses = state.MouseUses ?? [];
                _shown = (state.Shown ?? []).ToHashSet();
            }
        }
        catch (Exception failure) when (
            failure is IOException or UnauthorizedAccessException or JsonException)
        {
            // expected: 提示进度文件坏了或读不了——当作一次都没提示过，
            // 多提示一次严格好过挡住动作。
        }
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(new State(_mouseUses!, [.. _shown!])));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // expected: 与读同一副筹码——进度是锦上添花，绝不关键。
        }
    }
}
