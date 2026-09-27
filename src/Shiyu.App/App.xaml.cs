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
    private ImageArchive? _images;
    private System.Windows.Threading.DispatcherTimer? _retention;
    private SettingsWindow? _settingsWindow;
    private ThemeManager? _theme;
    private BarWindow? _bar;
    private AppIconCache? _icons;
    private FileTypeIcons? _fileIcons;
    private System.Windows.Threading.DispatcherTimer? _barGeometrySave;
    private WinVHook? _winV;
    private SpeechSynthesis? _speech;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The staged copy of an update runs with this argument: it swaps the
        // install and restarts the real application, showing nothing. Before
        // anything else — a finalizer has no business owning a clipboard
        // listener or a tray icon for the seconds it lives. Environment.Exit
        // rather than Shutdown: OnExit tears down windows this path never
        // built, and by then the staging reset has removed the staged copy's
        // own DLLs, so even JITting that teardown method would crash.
        if (UpdateService.TryRunFinalizer(e.Args))
        {
            Environment.Exit(0);
        }

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
        AppPaths.UseDirectory(_settings.DataDirectoryOverride);

        // A probe convenience in the same family as SHIYU_OPEN_SETTINGS: run
        // against an isolated data directory so automated checks never touch a
        // real history. Deliberately after the settings line — the settings
        // file always wins in real use, the variable only exists for probes.
        if (Environment.GetEnvironmentVariable("SHIYU_DATA_DIR") is { Length: > 0 } dataDirectory)
        {
            AppPaths.UseDirectory(dataDirectory);
        }

        // Same family again: point the public relay at a local worker so the
        // whole channel can be probed without a cloud deployment. Deliberately
        // in-memory only — the settings file keeps the official endpoint.
        if (Environment.GetEnvironmentVariable("SHIYU_RELAY_URL") is { Length: > 0 } relayUrl)
        {
            _settings = _settings with { RelayEndpoint = relayUrl };
        }

        // The relay's device identity: an anonymous install id, generated once
        // and stable for the machine's life. Persisted right away — a new id
        // every launch would quietly double the device's daily quota draw.
        if (_settings.RelayClientId.Length == 0)
        {
            _settings = _settings with { RelayClientId = Guid.NewGuid().ToString("N") };
            _settings.Save(AppPaths.SettingsFile);
        }

        // The finalizer's unfinished chore: it cannot delete the staged
        // directory it was running from. By now it has exited.
        UpdateStaging.CleanStagedIfIdle(AppPaths.DataDirectory);
        _store = EntryStore.Open(AppPaths.DatabaseFile);
        _icons = new AppIconCache(_store);
        _fileIcons = new FileTypeIcons();

        // Before any window exists: the first frame a window ever shows must
        // already be in the right theme.
        _theme = new ThemeManager();
        _theme.Apply(_settings.Theme);

        // One hidden window serves both the clipboard notifications and the
        // tray icon's callbacks — and, later, the global hotkeys.
        _messageWindow = new MessageWindow();
        _clipboard = new WindowsClipboardMonitor(_messageWindow);
        _exclusions = _settings.BuildExclusionPolicy();
        _images = new ImageArchive(AppPaths.ImageDirectory);
        _pipeline = new ClipboardPipeline(
            _clipboard, _store, TimeProvider.System, _exclusions, _images,
            icons: new SourceIconCache(_store, new WindowsSourceIcons()))
        {
            RecordImages = _settings.RecordImages,
            RecordFiles = _settings.RecordFiles,
        };
        _pipeline.ImageFailed += reason
            => _tray?.ShowNotification("拾语", $"复制的图片没能保存：{reason}");

        _tray = new TrayIcon(_messageWindow, "拾语")
        {
            RecentItems = () => _store.Recent(limit: 10).Select(entry => entry.Text).ToList(),
        };
        _pipeline.BadgeDeserved += ShowBadge;

        _tray.QuitRequested += Shutdown;
        _tray.OpenLibraryRequested += ShowLibrary;
        _tray.OpenSettingsRequested += ShowSettings;
        _tray.UpdateCheckRequested += ShowUpdateWindow;

        _writer = new WindowsClipboardWriter(_messageWindow);

        _capturePlatform = new WindowsCapturePlatform(_messageWindow, _writer);
        _capture = new SelectionCapture(_capturePlatform);
        _hotkeys = new HotkeyRegistry(_messageWindow);
        RegisterHotkeys();
        ApplyWinVTakeover();

        _singleInstance.WatchForOtherInstances(_messageWindow);

        // Starting Shiyu again is how a user who forgot it was running asks to
        // see it, so bring the library up rather than only saying "already
        // running" and leaving them no further along.
        _singleInstance.AnotherInstanceStarted += ShowLibrary;

        StartRetention();
        ApplyStartupPreference();
        StartUpdateWatch();

        // A probe convenience: SHIYU_OPEN_SETTINGS=1 opens the settings window
        // at startup, so automated checks can drive it without hunting for the
        // tray icon. Harmless for a user who never sets the variable.
        if (Environment.GetEnvironmentVariable("SHIYU_OPEN_SETTINGS") == "1")
        {
            ShowSettings();
        }

        // Same idea for the updater: open the update window at startup so
        // automated checks can drive the manual flow end to end.
        if (Environment.GetEnvironmentVariable("SHIYU_OPEN_UPDATE") == "1")
        {
            ShowUpdateWindow();
        }

        // The first-run guide asks the few things only the user knows. A
        // history that already exists says this is not a first run — the
        // guide stays away and never nags an upgrading user.
        if (!_settings.OnboardingCompleted && _store.Count() == 0)
        {
            var wizard = new OnboardingWindow(_settings, settings =>
            {
                _settings = settings;
                _settings.Save(AppPaths.SettingsFile);
                _exclusions = settings.BuildExclusionPolicy();
                _pipeline?.UseExclusions(_exclusions);
                if (_pipeline is not null)
                {
                    _pipeline.RecordImages = settings.RecordImages;
                    _pipeline.RecordFiles = settings.RecordFiles;
                }
            });
            wizard.Show();
        }
    }

    /// <summary>
    /// The manual entrance to the updater: one window, from the tray menu.
    /// </summary>
    private void ShowUpdateWindow()
    {
        new UpdateWindow(new UpdateService(AppPaths.DataDirectory)) { Owner = null }.Show();
    }

    /// <summary>
    /// One quiet check, shortly after startup, when the setting allows it.
    /// Finding something new raises a tray notification and nothing else —
    /// installing is the user's click, never ours. The listener and
    /// everything else keeps running throughout; the check touches only the
    /// network and the staging directory.
    /// </summary>
    private void StartUpdateWatch()
    {
        if (!_settings.UpdateAutoCheck)
        {
            return;
        }

        var deferred = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5),
        };

        deferred.Tick += async (_, _) =>
        {
            deferred.Stop();

            try
            {
                var updates = new UpdateService(AppPaths.DataDirectory);
                if (await updates.CheckAsync() is { } release && release.IsNewerThan(UpdateService.Current))
                {
                    _tray?.ShowNotification(
                        "拾语有新版本",
                        $"v{release.Version.Text} 已发布。右键托盘图标 → 检查更新 安装。");
                }
            }
            catch (Exception)
            {
                // A quiet check that cannot reach the channel says nothing:
                // there is nothing the user could act on from a balloon.
            }
        };

        deferred.Start();
    }

    /// <summary>
    /// Makes Windows agree with the setting. A tray tool nobody starts is a tray
    /// tool nobody has, so this is on by default — but only ever written when
    /// it actually differs, so a user who turned it off is not fought with on
    /// every launch.
    /// </summary>
    private void ApplyStartupPreference()
    {
        if (StartupRegistration.IsEnabled() == _settings.StartWithWindows)
        {
            return;
        }

        StartupRegistration.Set(_settings.StartWithWindows, Environment.ProcessPath ?? string.Empty);
    }

    /// <summary>
    /// Sweeps expired image originals now and once a day thereafter.
    ///
    /// Run on a background thread: a machine left unused for months has a
    /// backlog to work through, and doing it on the thread that draws would
    /// make startup look like a hang.
    /// </summary>
    private void StartRetention()
    {
        Sweep();

        _retention = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromHours(24),
        };
        _retention.Tick += (_, _) => Sweep();
        _retention.Start();

        void Sweep()
        {
            if (_store is null || _images is null)
            {
                return;
            }

            var service = new RetentionService(_store, _images, TimeProvider.System);
            var days = Math.Max(1, _settings.ImageRetentionDays);

            Task.Run(() =>
            {
                try
                {
                    service.Sweep(
                        TimeSpan.FromDays(days),
                        _settings.ProtectEntries && _settings.ProtectFavorites,
                        _settings.ProtectEntries && _settings.ProtectPinned);
                }
                catch (Exception)
                {
                    // Housekeeping failing is not worth interrupting the user
                    // over; the next sweep will try again.
                }
            });
        }
    }

    /// <summary>
    /// Opens the settings, and applies whatever comes back without a restart —
    /// except the data location, which by its nature cannot change underneath a
    /// running database.
    /// </summary>
    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(
            _settings,
            Apply,
            new BackupUi(
                _store!,
                AppPaths.ImageDirectory,
                () => File.Exists(AppPaths.SettingsFile) ? File.ReadAllText(AppPaths.SettingsFile) : null,
                RestoreSettingsFromBackup));
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();

        void Apply(AppSettings updated)
        {
            var movedData = updated.DataDirectoryOverride != _settings.DataDirectoryOverride;

            _settings = updated;
            _settings.Save(AppPaths.SettingsFile);

            // Applied immediately, no restart: swapping the token dictionary
            // re-resolves every DynamicResource in every open window.
            _theme?.Apply(updated.Theme);

            // The bar's density knobs take effect on the spot too.
            _bar?.ApplySettings(updated);

            // Rules are swapped in on the live policy object, so the very next
            // copy is judged by them.
            _exclusions = updated.BuildExclusionPolicy();
            _pipeline?.UseExclusions(_exclusions);
            if (_pipeline is not null)
            {
                _pipeline.RecordImages = updated.RecordImages;
                _pipeline.RecordFiles = updated.RecordFiles;
            }

            // Hotkeys are dropped and taken again as a set: working out which
            // individual ones changed would be more code than redoing all three.
            _hotkeys?.Dispose();
            _hotkeys = new HotkeyRegistry(_messageWindow!);
            RegisterHotkeys();
            ApplyWinVTakeover();

            if (movedData)
            {
                _tray?.ShowNotification("拾语", "数据位置已更改，重启拾语后生效。");
            }
        }

        /// <summary>
        /// A backup's settings land the way a saved settings window does:
        /// written to disk, then applied live — a restore should not have to
        /// wait for a restart to feel real.
        /// </summary>
        void RestoreSettingsFromBackup(string json)
        {
            File.WriteAllText(AppPaths.SettingsFile, json);
            var restored = AppSettings.Load(AppPaths.SettingsFile);

            _settings = restored;
            _theme?.Apply(restored.Theme);
            _bar?.ApplySettings(restored);
            _exclusions = restored.BuildExclusionPolicy();
            _pipeline?.UseExclusions(_exclusions);

            _hotkeys?.Dispose();
            _hotkeys = new HotkeyRegistry(_messageWindow!);
            RegisterHotkeys();
            ApplyWinVTakeover();
        }
    }

    /// <summary>
    /// Opens the settings window landed on one item — the deep link other
    /// windows use instead of knowing anything about settings internals.
    /// </summary>
    internal void OpenSettingsAt(string itemId)
    {
        ShowSettings();
        _settingsWindow?.JumpToItem(itemId);
    }

    /// <summary>
    /// Summons or hides the resident narrow bar. One instance, reused: a bar
    /// that keeps its position and scroll between summons is a place the user
    /// learns to find things.
    /// </summary>
    private void ToggleBar()
    {
        if (_store is null || _icons is null || _writer is null || _capture is null)
        {
            return;
        }

        if (_bar is null)
        {
            _bar = new BarWindow(_store, _icons, _writer, _capture, _settings, _fileIcons!);
            _bar.GeometryChanged += OnBarGeometryChanged;
            _bar.DataSettingsRequested += OpenSettingsAt;
            _bar.DeadDragNotice += notice => _tray?.ShowNotification("拾语", notice);

            // The header's pin writes its own setting (票 39): the bar hands
            // the updated record up, this side writes it down — the settings
            // page flows the other way through ApplySettings, so the two
            // editors never loop.
            _bar.SettingsChanged += settings =>
            {
                _settings = settings;
                _settings.Save(AppPaths.SettingsFile);
            };
        }

        _bar.Toggle();
    }

    /// <summary>
    /// Geometry saves are debounced rather than per-move: a drag fires this
    /// dozens of times a second and the settings file does not deserve that.
    /// </summary>
    private void OnBarGeometryChanged()
    {
        _barGeometrySave ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800),
        };

        _barGeometrySave.Tick -= SaveBarGeometry;
        _barGeometrySave.Tick += SaveBarGeometry;
        _barGeometrySave.Stop();
        _barGeometrySave.Start();
    }

    private void SaveBarGeometry(object? sender, EventArgs e)
    {
        _barGeometrySave?.Stop();

        if (_bar is null)
        {
            return;
        }

        _settings = _settings with
        {
            BarLeft = _bar.BarLeft,
            BarTop = _bar.BarTop,
            BarHeight = _bar.BarHeight,
        };
        _settings.Save(AppPaths.SettingsFile);
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

        // 朗读服务与面板同寿命：一条专用 STA 线程，懒得起、起一次用到底。
        _speech ??= new SpeechSynthesis();

        _panel ??= new PanelWindow(
            _hotkeys, _writer, () => _settings.BuildTranslationBackend(), _settings,
            SaveTranslationToHistory,
            dictionary: BuildDictionary,
            speech: _speech);
        await _panel.TranslateAsync(text);
    }

    /// <summary>
    /// 单词词典的两条路（票 35）：英文单词走免费词典（600ms 预算内补上，
    /// 否则静默放弃）；中文单词走 LLM 词典化兜底；其余文本不吃词典卡。
    /// 词典兜底只认自备密钥——公共通道只有 /translate 一张脸，拿不到词典
    /// 化 prompt；没有自己的后端时兜底静默让位，英文词典卡不受影响。
    /// </summary>
    private IDictionaryApi? BuildDictionary(string word)
        => DictionaryWord.IsEnglishWord(word)
            ? new BudgetedDictionary(new FreeDictionaryApi())
            : DictionaryWord.IsChineseWord(word)
                ? new LlmDictionaryApi(new OpenAiCompatibleBackend(_settings.Backend))
                : null;

    /// <summary>
    /// Files a kept translation. The link is made only when the original was
    /// itself recorded — a selection captured straight off the screen never
    /// entered history, and inventing a link would be pointing at nothing.
    /// </summary>
    private void SaveTranslationToHistory(string original, string translated)
    {
        if (_store is null || _pipeline is null)
        {
            return;
        }

        var linked = _store.Recent(limit: 200)
            .FirstOrDefault(entry => entry.Text == original && entry.TranslatedFrom is null)
            ?.Id;

        _pipeline.RecordTranslation(translated, linked);
        _tray?.ShowNotification("拾语", "译文已存入历史。");
    }

    /// <summary>
    /// One library window, reused. Opening a second copy of the same history
    /// would be two views that immediately disagree with each other.
    /// </summary>
    private void ShowLibrary()
    {
        if (_store is null || _writer is null || _images is null)
        {
            return;
        }

        if (_library is null)
        {
            // 动作（总结/合并笔记）要的是通用流式模型，公共通道只有
            // /translate 一张脸——这里恒走自备密钥后端；没配密钥的用户点
            // 动作时由面板如实报"还没有配置"，不影响翻译本身。
            _library = new LibraryWindow(
                _store, _writer, _images!, () => new OpenAiCompatibleBackend(_settings.Backend), _icons!,
                () => _settings, _pipeline);
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
        _retention?.Stop();
        _theme?.Dispose();
        _settingsWindow?.Close();
        SaveBarGeometry(this, EventArgs.Empty);
        _bar?.Close();
        _quickBar?.CloseForGood();
        _panel?.CloseForGood();
        _badge?.CloseForGood();
        _speech?.Dispose();
        _tray?.Dispose();
        _pipeline?.Dispose();
        _clipboard?.Dispose();
        _messageWindow?.Dispose();
        _store?.Dispose();
        _hotkeys?.Dispose();
        _winV?.Dispose();
        _singleInstance?.Dispose();

        base.OnExit(e);
    }

    /// <summary>
    /// Installs or removes the Win+V takeover to match the setting, on the
    /// spot — no restart, and the unhook is immediate. The hook lives in this
    /// process, so if the process dies the key returns to Windows by itself.
    /// </summary>
    private void ApplyWinVTakeover()
    {
        if (_settings.TakeOverWinV)
        {
            if (_winV is null)
            {
                _winV = new WinVHook();
                _winV.Triggered += ToggleBar;
            }
        }
        else if (_winV is not null)
        {
            _winV.Dispose();
            _winV = null;
        }
    }

    private void RegisterHotkeys()
    {
        // Ctrl+Shift+Z: deliberately a combination whose modifiers the user is
        // still holding when it fires, so the released-modifier handling in the
        // capture platform is exercised every single time rather than only in
        // some configurations.
        var conflicts = new List<HotkeyConflict>();

        Add(_settings.CaptureHotkey, "划词翻译", TranslateSelection);

        // Ctrl+Shift+V sits next to the paste the user already knows.
        Add(_settings.QuickBarHotkey, "快速条", ShowQuickBar);

        // The resident narrow bar: summoned and hidden by the same key.
        Add(_settings.BarHotkey, "窄条", ToggleBar);

        // The escape hatch. Without it the user cannot tell a filter that
        // judged wrongly from a tool that broke, and has no way to insist.
        Add(_settings.ClipboardTranslateHotkey, "翻译剪贴板内容", TranslateClipboard);

        if (conflicts.Count > 0)
        {
            // Reported together rather than one balloon after another, and
            // never fatal: losing a hotkey to another application is ordinary.
            _tray?.ShowNotification("拾语", string.Join("\n", conflicts.Select(c => c.Message)));
        }

        void Add(string spec, string description, Action action)
        {
            var parsed = HotkeySpec.Parse(spec);
            if (parsed is null)
            {
                // An unreadable setting should cost one feature, not startup.
                _tray?.ShowNotification("拾语", $"快捷键「{spec}」无法识别，{description} 暂时不可用。");
                return;
            }

            var hotkey = new Hotkey(Translate(parsed.Modifiers), parsed.Key, description);
            if (_hotkeys!.Register(hotkey, action) is { } conflict)
            {
                conflicts.Add(conflict);
            }
        }

        static HotkeyModifiers Translate(HotkeyModifier modifiers)
        {
            var result = HotkeyModifiers.None;
            if (modifiers.HasFlag(HotkeyModifier.Control)) result |= HotkeyModifiers.Control;
            if (modifiers.HasFlag(HotkeyModifier.Shift)) result |= HotkeyModifiers.Shift;
            if (modifiers.HasFlag(HotkeyModifier.Alt)) result |= HotkeyModifiers.Alt;
            if (modifiers.HasFlag(HotkeyModifier.Windows)) result |= HotkeyModifiers.Windows;
            return result;
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
