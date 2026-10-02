using System.Windows;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>管理窗模块（O-40 拆自 App.xaml.cs）：一扇常驻库窗的显隐与复用。</summary>
internal sealed class LibraryModule
{
    private AppShell? _shell;
    private LibraryWindow? _library;

    public void Attach(AppShell shell) => _shell = shell;

    /// <summary>
    /// One library window, reused. Opening a second copy of the same history
    /// would be two views that immediately disagree with each other.
    /// </summary>
    public void Show()
    {
        var shell = _shell!;

        if (shell.Writer is null)
        {
            return;
        }

        if (_library is null)
        {
            // 动作（总结/合并/改写/建议标签）与批量翻译要的是通用模型，
            // 与面板共用同一个后端工厂（票 08）：预设的附加字段与温度
            // 规则对它们一视同仁。没配密钥时按钮在管理窗侧禁用并说明。
            _library = new LibraryWindow(
                shell.Store, shell.Writer, shell.Images, shell.BuildStreamingModel!,
                shell.Icons, () => shell.Settings, shell.Pipeline);
            _library.Closed += (_, _) => _library = null;
            _library.Show();
        }
        else
        {
            _library.Reload();
            if (_library.WindowState == WindowState.Minimized)
            {
                _library.WindowState = WindowState.Normal;
            }

            _library.Activate();
        }
    }
}
