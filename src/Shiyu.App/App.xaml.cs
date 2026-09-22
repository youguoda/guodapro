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
    private SingleInstance? _singleInstance;
    private ExclusionPolicy? _exclusions;
    private WindowsClipboardWriter? _writer;
    private LibraryWindow? _library;
    private SelectionCapture? _capture;
    private HotkeyRegistry? _hotkeys;

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
        _singleInstance = SingleInstance.Acquire("Shiyu");
        if (!_singleInstance.IsOnlyInstance)
        {
            // Before anything else touches the clipboard or the history: a
            // rejected second instance must leave no trace behind it.
            _singleInstance.NotifyExistingInstance();
            Shutdown();
            return;
        }

        _store = EntryStore.Open(AppPaths.DatabaseFile);

        // One hidden window serves both the clipboard notifications and the
        // tray icon's callbacks — and, later, the global hotkeys.
        _messageWindow = new MessageWindow();
        _clipboard = new WindowsClipboardMonitor(_messageWindow);
        _exclusions = ExclusionPolicy.WithPresets();
        _pipeline = new ClipboardPipeline(_clipboard, _store, TimeProvider.System, _exclusions);

        _tray = new TrayIcon(_messageWindow, "拾语")
        {
            RecentItems = () => _store.Recent(limit: 10).Select(entry => entry.Text).ToList(),
        };
        _tray.QuitRequested += Shutdown;
        _tray.OpenLibraryRequested += ShowLibrary;

        _writer = new WindowsClipboardWriter(_messageWindow);

        _capture = new SelectionCapture(new WindowsCapturePlatform(_messageWindow, _writer));
        _hotkeys = new HotkeyRegistry(_messageWindow);

        // Ctrl+Shift+Z: deliberately a combination whose modifiers the user is
        // still holding when it fires, so the released-modifier handling in the
        // capture platform is exercised every single time rather than only in
        // some configurations.
        var conflict = _hotkeys.Register(
            new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'Z', "取词"),
            CaptureSelection);

        if (conflict is not null)
        {
            _tray.ShowNotification("拾语", conflict.Message);
        }

        _singleInstance.WatchForOtherInstances(_messageWindow);

        // Starting Shiyu again is how a user who forgot it was running asks to
        // see it, so bring the library up rather than only saying "already
        // running" and leaving them no further along.
        _singleInstance.AnotherInstanceStarted += ShowLibrary;
    }

    /// <summary>
    /// One library window, reused. Opening a second copy of the same history
    /// would be two views that immediately disagree with each other.
    /// </summary>
    private void ShowLibrary()
    {
        if (_store is null || _writer is null)
        {
            return;
        }

        if (_library is null)
        {
            _library = new LibraryWindow(_store, _writer);
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
        _hotkeys?.Dispose();
        _singleInstance?.Dispose();

        base.OnExit(e);
    }

    /// <summary>
    /// Captures whatever is selected in the foreground application and, for
    /// now, simply shows it. Translation hangs off this in issue 06.
    /// </summary>
    private void CaptureSelection()
    {
        if (_capture is null || _tray is null)
        {
            return;
        }

        var result = _capture.Capture();

        if (!result.ClipboardRestored)
        {
            // The one failure worth interrupting the user for: their own
            // clipboard is gone and they would otherwise find out by pasting
            // the wrong thing somewhere that matters.
            _tray.ShowNotification("拾语", "取词后未能还原你原本的剪贴板内容。");
            return;
        }

        switch (result.Outcome)
        {
            case CaptureOutcome.Captured:
                _tray.ShowNotification("取到的文字", result.Text!);
                break;
            case CaptureOutcome.NothingCaptured:
                _tray.ShowNotification("拾语", "没有取到文字：可能没有选中内容，或该程序响应太慢。");
                break;
            case CaptureOutcome.ClipboardUnavailable:
                _tray.ShowNotification("拾语", "剪贴板正被其他程序占用，稍后再试。");
                break;
        }
    }
}
