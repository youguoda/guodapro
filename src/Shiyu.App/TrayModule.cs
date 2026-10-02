using System.Linq;
using System.Windows;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>
/// 托盘与托盘菜单（O-40 拆自 App.xaml.cs）：托盘的建造、菜单事件的转达，
/// 以及托盘起来那一刻要说的两句话——坏设置文件的隔离通知、公共通道未上
/// 线的存量迁移提示。
/// </summary>
internal sealed class TrayModule
{
    /// <summary>
    /// Builds the tray and wires its menu. <paramref name="quarantineNotice"/>
    /// is the one sentence the settings load deferred until a tray existed to
    /// say it in — said once, then dropped.
    /// </summary>
    public void Attach(AppShell shell, string? quarantineNotice)
    {
        var tray = new TrayIcon(shell.MessageWindow, "拾语")
        {
            RecentItems = () => shell.Store.Recent(limit: 10).Select(entry => entry.Text).ToList(),
        };

        shell.Tray = tray;

        tray.QuitRequested += Application.Current.Shutdown;

        // The menu is the manual entrance to everything the hotkeys also reach:
        // each item goes through the shell's relay slots, never at a module.
        tray.OpenLibraryRequested += () => shell.ShowLibrary?.Invoke();
        tray.OpenSettingsRequested += () => shell.ShowSettings?.Invoke();
        tray.UpdateCheckRequested += () => shell.ShowUpdateWindow?.Invoke();

        // An unparseable settings file was renamed aside, not overwritten:
        // that deserves one honest sentence once a tray exists to say it in.
        if (quarantineNotice is { } notice)
        {
            tray.ShowNotification("拾语", notice);
        }

        // 公共通道未上线的存量迁移（票 08/ADR-0009）：选中公共通道的用户
        // 只提示这一次——有自备密钥就切过去；没有就保留选择（设置里显示为
        // 「即将推出」），翻译时由面板给配置引导卡。走 store 增量写。
        var settings = shell.Settings;
        if (settings.TranslationBackend == TranslationBackendKind.Relay
            && !settings.RelayUnavailableNoticed)
        {
            var hasOwnKey = settings.Backend.IsConfigured;
            if (shell.TryUpdateSettings(s => s with
            {
                TranslationBackend = hasOwnKey
                    ? TranslationBackendKind.OwnKey
                    : s.TranslationBackend,
                RelayUnavailableNoticed = true,
            }))
            {
                tray.ShowNotification("拾语", hasOwnKey
                    ? "公共翻译通道还未开放，已改用你自己的密钥翻译。"
                    : "公共翻译通道还未开放；翻译前请在 设置 → 服务 配置自己的密钥（有免费的预设可选）。");
            }
        }
    }
}
