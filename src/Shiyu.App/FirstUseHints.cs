using System.IO;
using System.Text.Json;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// The first-use footer hints: a brand-new user gets told about hover trays,
/// Ctrl key badges and the Enter/number/Esc keyboard model, for the first few
/// summons — and never again once the feature has been used three times (the
/// user has obviously found it) or the summons ran out. State persists beside
/// the history in the data directory, so a reinstall does not nag twice.
/// </summary>
internal static class FirstUseHints
{
    private sealed record State(int SummonsLeft, int ActionsSeen);

    private static string FilePath => Path.Combine(AppPaths.DataDirectory, "hints.json");

    private static int _summonsLeft = 5;
    private static int _actionsSeen;
    private static bool _loaded;
    private static int _rotation;

    /// <summary>Whether the next summon should carry a hint.</summary>
    public static bool ShowOnSummon
    {
        get
        {
            Load();
            return _summonsLeft > 0;
        }
    }

    /// <summary>Counts one hinted summon down.</summary>
    public static void RegisterSummon()
    {
        Load();
        if (_summonsLeft > 0)
        {
            _summonsLeft--;
            Save();
        }
    }

    /// <summary>Counts one real action up; three mean the user has found it.</summary>
    public static void RegisterAction()
    {
        Load();
        if (_summonsLeft > 0 && _actionsSeen < 3)
        {
            _actionsSeen++;
            if (_actionsSeen >= 3)
            {
                _summonsLeft = 0;
            }

            Save();
        }
    }

    /// <summary>The hint for this summon; two lines rotate so it stays fresh.</summary>
    public static string Text()
    {
        Load();
        return _rotation++ % 2 == 0
            ? "悬停卡片或按住 Ctrl 查看动作"
            : "Enter 粘贴 · 数字键直达 · Esc 分层退出";
    }

    private static void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            if (File.Exists(FilePath))
            {
                var state = JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath));
                if (state is not null)
                {
                    _summonsLeft = Math.Clamp(state.SummonsLeft, 0, 5);
                    _actionsSeen = Math.Clamp(state.ActionsSeen, 0, 3);
                }
            }
        }
        catch (Exception)
        {
            // A broken hints file costs nothing: defaults re-show the hints,
            // which is strictly better than blocking startup.
        }
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new State(_summonsLeft, _actionsSeen)));
        }
        catch (Exception)
        {
            // Same stakes as the load: hints are cosmetic, never critical.
        }
    }
}
