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
    private BadgeWindow? _badge;
    private PanelWindow? _panel;
    private AppSettings _settings = new();
    private WindowsCapturePlatform? _capturePlatform;
    private QuickBarWindow? _quickBar;

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

        _settings = AppSettings.Load(AppPaths.SettingsFile);
        _store = EntryStore.Open(AppPaths.DatabaseFile);

        // One hidden window serves both the clipboard notifications and the
        // tray icon's callbacks — and, later, the global hotkeys.
        _messageWindow = new MessageWindow();
        _clipboard = new WindowsClipboardMonitor(_messageWindow);
        _exclusions = _settings.BuildExclusionPolicy();
        _pipeline = new ClipboardPipeline(_clipboard, _store, TimeProvider.System, _exclusions);

        _tray = new TrayIcon(_messageWindow, "拾语")
        {
            RecentItems = () => _store.Recent(limit: 10).Select(entry => entry.Text).ToList(),
        };
        _pipeline.BadgeDeserved += ShowBadge;

        _tray.QuitRequested += Shutdown;
        _tray.OpenLibraryRequested += ShowLibrary;

        _writer = new WindowsClipboardWriter(_messageWindow);

        _capturePlatform = new WindowsCapturePlatform(_messageWindow, _writer);
        _capture = new SelectionCapture(_capturePlatform);
        _hotkeys = new HotkeyRegistry(_messageWindow);
        RegisterHotkeys();

        _singleInstance.WatchForOtherInstances(_messageWindow);

        // Starting Shiyu again is how a user who forgot it was running asks to
        // see it, so bring the library up rather than only saying "already
        // running" and leaving them no further along.
        _singleInstance.AnotherInstanceStarted += ShowLibrary;
    }

    /// <summary>
    /// One badge window, reused. It appears many times an hour; building a
    /// window each time is work the user would feel.
    /// </summary>
    private void ShowBadge(string text)
    {
        if (_badge is null)
        {
            _badge = new BadgeWindow();
            _badge.Accepted += ShowPanel;
        }

        _badge.Offer(text);
    }

    /// <summary>
    /// Summons the quick bar. One instance, reused: it appears dozens of times
    /// a day and building a window each time is work the user would feel.
    /// </summary>
    private void ShowQuickBar()
    {
        if (_store is null || _capture is null)
        {
            return;
        }

        _quickBar ??= new QuickBarWindow(_store, _capture);
        _quickBar.Summon();
    }

    /// <summary>
    /// Opens the panel on the given text. One panel, reused: a second copy
    /// would be two translations of two different things competing for the
    /// same corner of the screen.
    /// </summary>
    private async void ShowPanel(string text)
    {
        if (_hotkeys is null || _writer is null)
        {
            return;
        }

        _panel ??= new PanelWindow(_hotkeys, _writer, () => new OpenAiCompatibleBackend(_settings.Backend), _settings);
        await _panel.TranslateAsync(text);
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
        _quickBar?.CloseForGood();
        _panel?.CloseForGood();
        _badge?.CloseForGood();
        _tray?.Dispose();
        _pipeline?.Dispose();
        _clipboard?.Dispose();
        _messageWindow?.Dispose();
        _store?.Dispose();
        _hotkeys?.Dispose();
        _singleInstance?.Dispose();

        base.OnExit(e);
    }

    private void RegisterHotkeys()
    {
        // Ctrl+Shift+Z: deliberately a combination whose modifiers the user is
        // still holding when it fires, so the released-modifier handling in the
        // capture platform is exercised every single time rather than only in
        // some configurations.
        var conflicts = new List<HotkeyConflict>();

        Add(new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'Z', "划词翻译"),
            TranslateSelection);

        // Ctrl+Shift+V sits next to the paste the user already knows.
        Add(new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'V', "快速条"),
            ShowQuickBar);

        // The escape hatch. Without it the user cannot tell a filter that
        // judged wrongly from a tool that broke, and has no way to insist.
        Add(new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'X', "翻译剪贴板内容"),
            TranslateClipboard);

        if (conflicts.Count > 0)
        {
            // Reported together rather than one balloon after another, and
            // never fatal: losing a hotkey to another application is ordinary.
            _tray?.ShowNotification("拾语", string.Join("\n", conflicts.Select(c => c.Message)));
        }

        void Add(Hotkey hotkey, Action action)
        {
            if (_hotkeys!.Register(hotkey, action) is { } conflict)
            {
                conflicts.Add(conflict);
            }
        }
    }

    /// <summary>Captures what is selected in the foreground application and translates it.</summary>
    private void TranslateSelection()
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
                ShowPanel(result.Text!);
                break;

            case CaptureOutcome.NothingCaptured:
                // All three causes look identical from out here, so the message
                // names them rather than asserting one. The third is the one a
                // user would never guess: a window running as administrator
                // silently discards synthesised keystrokes from a program that
                // is not, so capture simply never gets an answer.
                _tray.ShowNotification(
                    "拾语",
                    "没有取到文字。可能是没有选中内容、该程序响应太慢，"
                    + "或它以管理员身份运行——那种窗口会丢弃拾语发出的按键。"
                    + "可以复制后按翻译剪贴板的快捷键。");
                break;

            case CaptureOutcome.ClipboardUnavailable:
                _tray.ShowNotification("拾语", "剪贴板正被其他程序占用，稍后再试。");
                break;
        }
    }

    /// <summary>
    /// The escape hatch: translate whatever is on the clipboard right now,
    /// whether or not the badge ever offered to.
    /// </summary>
    private void TranslateClipboard()
    {
        if (_capturePlatform is null || _tray is null)
        {
            return;
        }

        string? text;
        try
        {
            text = _capturePlatform.ReadClipboardText();
        }
        catch (ClipboardUnavailableException)
        {
            _tray.ShowNotification("拾语", "剪贴板正被其他程序占用，稍后再试。");
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            _tray.ShowNotification("拾语", "剪贴板里没有可翻译的文字。");
            return;
        }

        // Deliberately not passed through the badge's filter: insisting is the
        // entire point of this hotkey.
        ShowPanel(text);
    }
}
