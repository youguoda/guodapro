using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// 窄条模块（O-40 拆自 App.xaml.cs）：常驻窄条与快速条的显隐、几何的防抖
/// 保存——几何保存进设置走 store，视觉状态从 ApplySettings 广播回来。
/// </summary>
internal sealed class BarModule
{
    private AppShell? _shell;
    private BarWindow? _bar;
    private QuickBarWindow? _quickBar;
    private System.Windows.Threading.DispatcherTimer? _geometrySave;

    public void Attach(AppShell shell)
    {
        _shell = shell;

        // The bar's density knobs take effect on the spot（原单体应用函数的
        // 一段，O-20）。
        shell.SettingsChanged += s => _bar?.ApplySettings(s);
    }

    /// <summary>
    /// Summons or hides the resident narrow bar. One instance, reused: a bar
    /// that keeps its position and scroll between summons is a place the user
    /// learns to find things.
    /// </summary>
    public void Toggle()
    {
        var shell = _shell!;

        if (shell.Writer is null || shell.Capture is null)
        {
            return;
        }

        if (_bar is null)
        {
            _bar = new BarWindow(
                shell.Store, shell.Icons, shell.Writer, shell.Capture, shell.Settings,
                shell.FileIcons, shell.FileProbe);
            _bar.GeometryChanged += OnBarGeometryChanged;

            // 深链进设置的数据页：条不知道设置的内部，只知道条目 Id。
            _bar.DataSettingsRequested += itemId => shell.OpenSettingsAt?.Invoke(itemId);
            _bar.DeadDragNotice += notice => shell.TellUser(notice);

            // 品牌钮打开管理窗（票 21 §6.1 第 1 行）：窄条管"拿回"，
            // 整理归管理窗。
            _bar.LibraryRequested += () => shell.ShowLibrary?.Invoke();

            // The header's pin reports only what it wants (票 39/O-20): this
            // side turns it into a one-field update through the store, and
            // the pin's visual state comes back via ApplySettings when the
            // store broadcasts — the bar never writes settings itself again,
            // so its snapshot can no longer erase anyone else's changes (S1/S2).
            _bar.TopmostWanted += wanted =>
                shell.TryUpdateSettings(s => s with { BarAlwaysOnTop = wanted });
        }

        _bar.Toggle();
    }

    /// <summary>Summons the quick bar. One instance, reused: it appears dozens of times
    /// a day and building a window each time is work the user would feel.</summary>
    public void ShowQuickBar()
    {
        var shell = _shell!;

        if (shell.Capture is null)
        {
            return;
        }

        _quickBar ??= new QuickBarWindow(shell.Store, shell.Capture);
        _quickBar.Summon();
    }

    /// <summary>
    /// Geometry saves are debounced rather than per-move: a drag fires this
    /// dozens of times a second and the settings file does not deserve that.
    /// </summary>
    private void OnBarGeometryChanged()
    {
        _geometrySave ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800),
        };

        _geometrySave.Tick -= SaveBarGeometry;
        _geometrySave.Tick += SaveBarGeometry;
        _geometrySave.Stop();
        _geometrySave.Start();
    }

    private void SaveBarGeometry(object? sender, EventArgs e)
    {
        _geometrySave?.Stop();

        var shell = _shell!;
        if (_bar is null)
        {
            return;
        }

        // An unchanged geometry — the common case at shutdown — skips the
        // write entirely: every store update re-applies settings everywhere,
        // and exit has no use for that.
        var settings = shell.Settings;
        if (settings.BarLeft == _bar.BarLeft
            && settings.BarTop == _bar.BarTop
            && settings.BarHeight == _bar.BarHeight)
        {
            return;
        }

        shell.TryUpdateSettings(s => s with
        {
            BarLeft = _bar.BarLeft,
            BarTop = _bar.BarTop,
            BarHeight = _bar.BarHeight,
        });
    }

    /// <summary>原 OnExit 的中段：先落几何（与退出时完全同一函数），再关两扇条窗。</summary>
    public void Shutdown()
    {
        SaveBarGeometry(this, EventArgs.Empty);
        _bar?.Close();
        _quickBar?.CloseForGood();
    }
}
