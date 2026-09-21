using System.IO;
using System.Linq;
using System.Windows;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

public partial class App : Application
{
    private MessageWindow? _messageWindow;
    private WindowsClipboardMonitor? _clipboard;
    private EntryStore? _store;
    private ClipboardPipeline? _pipeline;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            Start();
        }
        catch (Exception exception)
        {
            // Shiyu has no window. Without this, a failure to start is a
            // process that silently isn't there — nothing to look at, nothing
            // to read. The file is the only way in.
            RecordStartupFailure(exception);
            throw;
        }
    }

    private void Start()
    {
        _store = EntryStore.Open(AppPaths.DatabaseFile);

        // One hidden window serves both the clipboard notifications and the
        // tray icon's callbacks — and, later, the global hotkeys.
        _messageWindow = new MessageWindow();
        _clipboard = new WindowsClipboardMonitor(_messageWindow);
        _pipeline = new ClipboardPipeline(_clipboard, _store, TimeProvider.System);

        _tray = new TrayIcon(_messageWindow, "拾语")
        {
            RecentItems = () => _store.Recent(limit: 10).Select(entry => entry.Text).ToList(),
        };
        _tray.QuitRequested += Shutdown;
    }

    private static void RecordStartupFailure(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(
                Path.Combine(AppPaths.DataDirectory, "startup-error.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}");
        }
        catch (IOException)
        {
            // Nothing useful left to do; let the original failure surface.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Reverse order of construction: the tray and the clipboard listener
        // both hold the message window.
        _tray?.Dispose();
        _pipeline?.Dispose();
        _clipboard?.Dispose();
        _messageWindow?.Dispose();
        _store?.Dispose();

        base.OnExit(e);
    }
}
