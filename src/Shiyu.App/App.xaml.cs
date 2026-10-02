using System.ComponentModel;
using System.Diagnostics;
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
    private SettingsWindow? _settingsWindow;
    private UpdateWindow? _updateWindow;
    private ThemeManager? _theme;
    private BarWindow? _bar;
    private AppIconCache? _icons;
    private FileTypeIcons? _fileIcons;
    private System.Windows.Threading.DispatcherTimer? _barGeometrySave;
    private WinVHook? _winV;
    private SpeechSynthesis? _speech;
    private MouseDragHook? _mouseDrag;
    private PanelWindow? _panel;
    private WindowsCapturePlatform? _capturePlatform;
    private QuickBarWindow? _quickBar;
    private ImageArchive? _images;
    private System.Windows.Threading.DispatcherTimer? _retention;

    /// <summary>
    /// 设置的唯一写入口（O-20）：一切读走 <see cref="Settings"/>，一切写走
    /// <see cref="SettingsStore.Update"/>——此前每个写入方攥着整份快照各自
    /// 写回，最后保存的一方获胜。
    /// </summary>
    private SettingsStore? _settingsStore;

    /// <summary>生效设置，永远是 store 的最新值。</summary>
    private AppSettings Settings => _settingsStore!.Current;

    /// <summary>上一次已应用的设置：Changed 处理器靠它分辨"数据位置是否刚被改过"。</summary>
    private AppSettings _appliedSettings = new();

    /// <summary>加载设置时若发生了坏文件迁移，托盘起来后要说一次的话。</summary>
    private string? _settingsQuarantineNotice;

    /// <summary>"前台在排除名单"的提示是否已经说过一次（O-17）。</summary>
    private bool _captureExcludedNotified;

    /// <summary>
    /// 划词路径挂起的剪贴板还原：取词借走了用户剪贴板，还原被推迟到面板
    /// 显示之后（票 37）。非 null 即"当前徽标是一次划词，且债未还"。
    /// </summary>
    private DeferredCapture? _pendingSelection;

    /// <summary>
    /// 探针模式（票 15）：调试构建 + <c>SHIYU_DATA_DIR</c> 指向隔离目录。
    /// 一切全局的东西——单实例名、热键、钩子、剪贴板监听、更新检查——
    /// 都让开，让探针实例和用户正在用的实例并排跑、互不打扰。发布构建
    /// 里它是常量 false，行为一字不变。
    /// </summary>
    private static bool IsProbe =>
#if DEBUG
        Environment.GetEnvironmentVariable("SHIYU_DATA_DIR") is { Length: > 0 };
#else
        false;
#endif

    /// <summary>
    /// 崩溃托盘提示的节流表（O-05）：同类异常 5 分钟内只打扰一次。键是
    /// 异常类型+消息的指纹，值是上次提示时间；过期项顺手清，表不设上限
    /// 就成了泄漏。
    /// </summary>
    private readonly Dictionary<string, DateTimeOffset> _crashNotices = new();

    private static readonly TimeSpan CrashNoticeInterval = TimeSpan.FromMinutes(5);

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

        // 先于一切业务接线（O-05）：处理器挂上之后，启动路径上的任何闪失
        // 才有日志与托盘兜底，而不是把进程直接带走——对一个托盘常驻的
        // 记录工具，崩溃就等于静默停止记录。
        InstallGlobalExceptionHandlers();

        try
        {
            Start();
        }
        catch (Exception exception)
        {
            // Shiyu has no window. Without this, a failure to start is a
            // process that silently isn't there — nothing to look at, nothing
            // to read. The file is the only way in.
            Log.Event(LogEvent.StartupFailed, exception);
            RecordStartupFailure(exception);
            throw;
        }
    }

    /// <summary>
    /// 三个进程级兜底（O-05）。UI 线程的异常记日志、托盘说一次、
    /// <c>Handled=true</c> 挺住继续跑；没人 await 的 Task 记下并认领
    /// （否则进程退出时它们会变成崩溃对话框）；其余线程的致命异常拦是
    /// 拦不住的——处理器返回后 CLR 仍会终止进程，sink 是同步写，这里
    /// 唯一能做的是把现场完整留在盘上再走。
    /// </summary>
    private void InstallGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Event(LogEvent.AppCrash, e.Exception);
            NoticeCrashOnce(e.Exception);
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Event(LogEvent.UnobservedTask, e.Exception);
            e.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                Log.Event(LogEvent.FatalExit, exception);
            }
        };
    }

    /// <summary>
    /// 崩溃的托盘提示：同类错误（类型+消息一致）5 分钟内只弹一次——一个
    /// 每 10 秒闪一次的循环只会教会用户永久关掉通知。别的错误照常说。
    /// </summary>
    private void NoticeCrashOnce(Exception exception)
    {
        var fingerprint = exception.GetType().FullName + ": " + exception.Message;
        var now = DateTimeOffset.Now;

        foreach (var stale in _crashNotices.Where(p => now - p.Value > CrashNoticeInterval).ToList())
        {
            _crashNotices.Remove(stale.Key);
        }

        if (_crashNotices.TryGetValue(fingerprint, out var last)
            && now - last <= CrashNoticeInterval)
        {
            return;
        }

        _crashNotices[fingerprint] = now;
        _tray?.ShowNotification("拾语", "拾语遇到一个错误，已记录到日志。");
    }

    private void Start()
    {
        // The probe decision precedes the mutex: the name is the whole trick
        // that lets a probe instance run beside the user's real one (票 15).
        // Same data directory => same mutex, so probes of one directory still
        // reject each other; a different directory never collides with "Shiyu".
        var mutexName = "Shiyu";
#if DEBUG
        if (Environment.GetEnvironmentVariable("SHIYU_DATA_DIR") is { Length: > 0 } probeDirectory)
        {
            mutexName = ProbeMutexName(probeDirectory);

            // Settings move into the probe directory too: reading the real
            // ones would aim the probe at the user's backend, and writing
            // them back (geometry, relay id) would reach the real file.
            AppPaths.UseProbeDirectory(probeDirectory);
            Trace.WriteLine($"probe mode: hotkeys/hooks off, data dir {probeDirectory}, mutex {mutexName}");
        }
#endif

        _singleInstance = SingleInstance.Acquire(mutexName);
        if (!_singleInstance.IsOnlyInstance)
        {
            // Before anything else touches the clipboard or the history: a
            // rejected second instance must leave no trace behind it.
            _singleInstance.NotifyExistingInstance();
            Shutdown();
            return;
        }

        // Everything reads and writes settings through one store (O-20). The
        // relay override stays a probe convenience riding the store's
        // non-persisting bypass: reads see it, the file never learns of it.
        var relayUrl = Environment.GetEnvironmentVariable("SHIYU_RELAY_URL");
        var loaded = SettingsStore.Load(
            AppPaths.SettingsFile,
            string.IsNullOrEmpty(relayUrl) ? null : s => s with { RelayEndpoint = relayUrl });
        _settingsStore = loaded.Store;
        AppPaths.UseDirectory(Settings.DataDirectoryOverride);

        // An unparseable settings file was renamed aside, not overwritten:
        // that deserves one honest sentence once a tray exists to say it in.
        if (loaded.QuarantinedPath is { } quarantined)
        {
            _settingsQuarantineNotice =
                "设置文件无法读取，已把原文件保留为 "
                + Path.GetFileName(quarantined) + "，并暂时使用默认设置。";
        }

        // A probe convenience in the same family as SHIYU_OPEN_SETTINGS: run
        // against an isolated data directory so automated checks never touch a
        // real history. Deliberately after the settings line — the settings
        // file always wins in real use, the variable only exists for probes.
        if (Environment.GetEnvironmentVariable("SHIYU_DATA_DIR") is { Length: > 0 } dataDirectory)
        {
            AppPaths.UseDirectory(dataDirectory);
        }

        // 数据目录定下来这刻起，一切后续失败都有处可写（O-05）。同步 sink：
        // 致命异常的最后一行必须在进程倒下之前落盘。
        Log.Attach(new FileLogSink(AppPaths.DataDirectory));

#if DEBUG
        // 实机验收用的隐藏命令（票 06）：在 DispatcherTimer.Tick 里抛一个
        // 异常，验证 UI 线程兜底——进程存活、日志一行、托盘提示一次。仅
        // 调试构建存在，与 SHIYU_DATA_DIR 同族，绝不进发布。
        if (Environment.GetEnvironmentVariable("SHIYU_DEBUG_TICK_CRASH") == "1")
        {
            var crash = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3),
            };
            crash.Tick += (_, _) =>
            {
                // 单发：验证的是"抛 + 存活 + 提示一次"，不是压测节流。
                crash.Stop();
                throw new InvalidOperationException("debug tick crash probe");
            };
            crash.Start();
        }
#endif

        // The relay's device identity: an anonymous install id, generated once
        // and stable for the machine's life. Persisted right away — a new id
        // every launch would quietly double the device's daily quota draw.
        // A failed write has nothing to show itself in yet; the id simply
        // regenerates next launch.
        if (Settings.RelayClientId.Length == 0)
        {
            try
            {
                _settingsStore.Update(
                    s => s with { RelayClientId = Guid.NewGuid().ToString("N") },
                    AppPaths.SettingsFile);
            }
            catch (SettingsSaveException)
            {
            }
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
        _theme.Apply(Settings.Theme);

        // One hidden window serves both the clipboard notifications and the
        // tray icon's callbacks — and, later, the global hotkeys.
        _messageWindow = new MessageWindow();
        _exclusions = Settings.BuildExclusionPolicy();
        _images = new ImageArchive(AppPaths.ImageDirectory);

        // A probe instance watches no clipboard: the user's copies must not
        // land in the probe's database (synthetic data only, ever — that is
        // what the screenshots are allowed to contain), and a copied English
        // sentence must not raise a translation badge from the probe process
        // onto the user's screen.
        if (!IsProbe)
        {
            _clipboard = new WindowsClipboardMonitor(_messageWindow);
            _pipeline = new ClipboardPipeline(
                _clipboard, _store, TimeProvider.System, _exclusions, _images,
                icons: new SourceIconCache(_store, new WindowsSourceIcons()))
            {
                RecordImages = Settings.RecordImages,
                RecordFiles = Settings.RecordFiles,
            };
            _pipeline.ImageFailed += reason
                => _tray?.ShowNotification("拾语", $"复制的图片没能保存：{reason}");
        }

        _tray = new TrayIcon(_messageWindow, "拾语")
        {
            RecentItems = () => _store.Recent(limit: 10).Select(entry => entry.Text).ToList(),
        };

        if (!IsProbe)
        {
            _pipeline!.BadgeDeserved += text => ShowBadge(text);
        }

        _tray.QuitRequested += Shutdown;
        _tray.OpenLibraryRequested += ShowLibrary;
        _tray.OpenSettingsRequested += ShowSettings;
        _tray.UpdateCheckRequested += ShowUpdateWindow;

        if (_settingsQuarantineNotice is { } notice)
        {
            _tray.ShowNotification("拾语", notice);
            _settingsQuarantineNotice = null;
        }

        // 公共通道未上线的存量迁移（票 08/ADR-0009）：选中公共通道的用户
        // 只提示这一次——有自备密钥就切过去；没有就保留选择（设置里显示为
        // 「即将推出」），翻译时由面板给配置引导卡。走 store 增量写。
        if (Settings.TranslationBackend == TranslationBackendKind.Relay
            && !Settings.RelayUnavailableNoticed)
        {
            var hasOwnKey = Settings.Backend.IsConfigured;
            if (TryUpdateSettings(s => s with
                {
                    TranslationBackend = hasOwnKey
                        ? TranslationBackendKind.OwnKey
                        : s.TranslationBackend,
                    RelayUnavailableNoticed = true,
                }))
            {
                _tray?.ShowNotification("拾语", hasOwnKey
                    ? "公共翻译通道还未开放，已改用你自己的密钥翻译。"
                    : "公共翻译通道还未开放；翻译前请在 设置 → 服务 配置自己的密钥（有免费的预设可选）。");
            }
        }

        _writer = new WindowsClipboardWriter(_messageWindow);

        _capturePlatform = new WindowsCapturePlatform(_messageWindow, _writer);
        _capture = new SelectionCapture(_capturePlatform);
        _hotkeys = new HotkeyRegistry(_messageWindow);
        RegisterHotkeys();
        ApplyWinVTakeover();
        ApplySelectionBadge();

        // One subscription, every writer (O-20): theme, bar, exclusions,
        // recording switches, hotkeys, Win+V and the selection badge all
        // re-apply from the single handler below. There used to be three
        // apply paths — settings save, onboarding, restore — each applying a
        // different subset, and they had drifted apart.
        _settingsStore.Changed += OnSettingsChanged;
        _appliedSettings = Settings;

        // The probe keeps out of the real instance's wake-up channel: the
        // "another instance started" broadcast goes to every Shiyu process,
        // and a probe answering it would throw a library window at the user.
        if (!IsProbe)
        {
            _singleInstance.WatchForOtherInstances(_messageWindow);

            // Starting Shiyu again is how a user who forgot it was running
            // asks to see it, so bring the library up rather than only saying
            // "already running" and leaving them no further along.
            _singleInstance.AnotherInstanceStarted += ShowLibrary;
        }

        StartRetention();
        ApplyStartupPreference();
        if (!IsProbe)
        {
            StartUpdateWatch();
        }

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
        // guide stays away and never nags an upgrading user. A probe skips it
        // on principle: the whole point is a deterministic, empty surface.
        if (!IsProbe && !Settings.OnboardingCompleted && _store.Count() == 0)
        {
            // The wizard writes through the store like everyone else, so what
            // it collects — hotkeys, theme, backend, rules — applies live via
            // the same OnSettingsChanged path a settings save takes, and a
            // bar summoned mid-guide can no longer erase it (S2).
            var wizard = new OnboardingWindow(Settings, _settingsStore);
            wizard.Show();
        }

#if DEBUG
        RunProbeCommand();
#endif
    }

#if DEBUG
    /// <summary>
    /// 探针的呼出通道（票 15）：热键一颗都没注册，所以窗口只能这样开——
    /// <c>SHIYU_PROBE_CMD=bar|panel|library|settings|quickbar</c> 启动即直接
    /// 显示对应窗口，走的是和热键完全相同的内部方法。
    /// <c>SHIYU_PROBE_ITEM</c> 让设置窗直接落到某个设置项（既有深链）；
    /// <c>SHIYU_PROBE_TEXT</c> 给面板一句要翻译的话。仅在探针模式生效。
    /// </summary>
    private void RunProbeCommand()
    {
        if (!IsProbe)
        {
            return;
        }

        var text = Environment.GetEnvironmentVariable("SHIYU_PROBE_TEXT");
        if (string.IsNullOrWhiteSpace(text))
        {
            text = "Placeholder sentence for probe 15.";
        }

        switch (Environment.GetEnvironmentVariable("SHIYU_PROBE_CMD"))
        {
            case "bar":
                ToggleBar();
                break;

            case "quickbar":
                ShowQuickBar();
                break;

            case "library":
                ShowLibrary();
                break;

            case "settings":
                if (Environment.GetEnvironmentVariable("SHIYU_PROBE_ITEM") is { Length: > 0 } item)
                {
                    OpenSettingsAt(item);
                }
                else
                {
                    ShowSettings();
                }
                break;

            case "panel":
                ShowPanel(text);
                break;
        }
    }

    /// <summary>
    /// 探针互斥量名：数据目录路径的稳定哈希。同一目录的探针仍互斥，
    /// 不同目录、以及与用户的 <c>Shiyu</c>，永不相撞。路径大写归一，
    /// 因为 Windows 把互斥量名当不区分大小写处理而目录写法人人不同。
    /// </summary>
    private static string ProbeMutexName(string dataDirectory)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(dataDirectory.Trim().ToUpperInvariant()));
        return "Shiyu-probe-" + Convert.ToHexString(hash)[..16];
    }
#endif

    /// <summary>
    /// The manual entrance to the updater: one window at a time, from the tray
    /// menu or the startup probe — a second click activates the first one
    /// instead of stacking a racing download beside it.
    /// </summary>
    private void ShowUpdateWindow()
    {
        if (_updateWindow is not null)
        {
            _updateWindow.Activate();
            return;
        }

        _updateWindow = new UpdateWindow(new UpdateService(AppPaths.DataDirectory));
        _updateWindow.Closed += (_, _) => _updateWindow = null;
        _updateWindow.Show();
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
        if (!Settings.UpdateAutoCheck)
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
                    Log.Event(LogEvent.UpdateChecked, ("found", true));
                    _tray?.ShowNotification(
                        "拾语有新版本",
                        $"v{release.Version.Text} 已发布。右键托盘图标 → 检查更新 安装。");
                }
                else
                {
                    Log.Event(LogEvent.UpdateChecked, ("found", false));
                }
            }
            catch (Exception failure)
            {
                // A quiet check that cannot reach the channel says nothing to
                // the user — 404 或断网没有可点的动作——但 O-24 要求留下
                // 一行日志，别让通道坏了只能靠猜。
                Log.Event(LogEvent.UpdateCheckFailed, failure);
            }
        };

        deferred.Start();
    }

    /// <summary>
    /// Makes Windows agree with the setting. A tray tool nobody starts is a tray
    /// tool nobody has, so this is on by default — but only ever written when
    /// it actually differs, so a user who turned it off is not fought with on
    /// every launch.
    ///
    /// Release builds only: autostart belongs to the installed copy, never to
    /// whatever a development build last left in bin. An entry that points at
    /// another location is taken over, so moving the install moves autostart.
    /// </summary>
    private void ApplyStartupPreference()
    {
#if !DEBUG
        var path = Environment.ProcessPath ?? string.Empty;
        if (StartupRegistration.IsEnabled() == Settings.StartWithWindows
            && (!Settings.StartWithWindows || StartupRegistration.PointsAt(path)))
        {
            return;
        }

        StartupRegistration.Set(Settings.StartWithWindows, path);
#endif
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
            var days = Math.Max(1, Settings.ImageRetentionDays);

            Task.Run(() =>
            {
                try
                {
                    var result = service.Sweep(
                        TimeSpan.FromDays(days),
                        Settings.ProtectEntries && Settings.ProtectFavorites,
                        Settings.ProtectEntries && Settings.ProtectPinned);
                    Log.Event(LogEvent.RetentionSwept, ("removed", result.Removed));
                }
                catch (Exception failure)
                {
                    // Housekeeping failing is not worth interrupting the user
                    // over; the next sweep will try again. 留一行日志（O-24）：
                    // 图片目录悄悄堆满往往只有它知道原因。
                    Log.Event(LogEvent.RetentionSweepFailed, failure, ("days", days));
                }
            });
        }
    }

    /// <summary>
    /// Opens the settings window. It reads from the store and submits only
    /// what the user changed; live effects come from the same
    /// <see cref="OnSettingsChanged"/> everyone else answers to.
    /// </summary>
    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(
            _settingsStore!,
            new BackupUi(
                _store!,
                AppPaths.ImageDirectory,
                () => File.Exists(AppPaths.SettingsFile) ? File.ReadAllText(AppPaths.SettingsFile) : null,
                RestoreSettingsFromBackup));
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();

        /// <summary>
        /// A backup's settings land the way every write does: through the
        /// store, whole-record, then applied live by the one changed handler.
        /// The settings window refreshes itself from that same event, so a
        /// later save can no longer roll the import back from a stale
        /// snapshot (S3).
        ///
        /// Called on the UI thread by BackupUi once the import has landed.
        /// Answers null when applied, or the plain-words reason the current
        /// settings were kept: an unparseable backup must not be written to
        /// disk, where a later load would quietly reset the user to defaults.
        /// </summary>
        string? RestoreSettingsFromBackup(string json)
        {
            if (!AppSettings.TryParse(json, out var restored))
            {
                return "条目已导入，但备份里的设置无法识别，已保留当前设置。";
            }

            try
            {
                _settingsStore!.Update(_ => restored, AppPaths.SettingsFile);
            }
            catch (SettingsSaveException)
            {
                // The store refused the change: memory and disk still hold
                // the pre-import settings, so that is exactly what the
                // summary should claim.
                return "条目已导入，但设置没能写入磁盘，已保留当前设置。";
            }

            return null;
        }
    }

    /// <summary>
    /// The one apply path (O-20): whatever changed the settings — the
    /// settings window, onboarding, a restore, the bar's pin, a geometry
    /// save — the effects land here, immediately, no restart.
    /// </summary>
    private void OnSettingsChanged(AppSettings updated)
    {
        var movedData = updated.DataDirectoryOverride != _appliedSettings.DataDirectoryOverride;
        _appliedSettings = updated;

        // Swapping the token dictionary re-resolves every DynamicResource in
        // every open window.
        _theme?.Apply(updated.Theme);

        // The bar's density knobs take effect on the spot; the panel's
        // languages follow without a restart.
        _bar?.ApplySettings(updated);
        _panel?.ApplySettings(updated);

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
        // individual ones changed would be more code than redoing all four.
        _hotkeys?.Dispose();
        _hotkeys = new HotkeyRegistry(_messageWindow!);
        RegisterHotkeys();
        ApplyWinVTakeover();
        ApplySelectionBadge();

        if (movedData)
        {
            _tray?.ShowNotification("拾语", "数据位置已更改，重启拾语后生效。");
        }
    }

    /// <summary>
    /// Writes one settings change through the store. A failed write becomes
    /// a tray event rather than an exception escaping a UI handler — and the
    /// store has already refused the change, so memory and disk still agree.
    /// </summary>
    private bool TryUpdateSettings(Func<AppSettings, AppSettings> mutate)
    {
        try
        {
            _settingsStore!.Update(mutate, AppPaths.SettingsFile);
            Log.Event(LogEvent.SettingsSaved);
            return true;
        }
        catch (SettingsSaveException failure)
        {
            Log.Event(LogEvent.SettingsSaveFailed, failure);
            _tray?.ShowNotification("拾语", failure.Message);
            return false;
        }
    }

    /// <summary>
    /// 子窗口往托盘说一句话的通道：它们没有托盘引用，也不该有——界面上
    /// "用户点了却什么都没发生"的失败，配得上一句人话（O-24）。
    /// </summary>
    internal void TellUser(string message) => _tray?.ShowNotification("拾语", message);

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
            _bar = new BarWindow(_store, _icons, _writer, _capture, Settings, _fileIcons!);
            _bar.GeometryChanged += OnBarGeometryChanged;
            _bar.DataSettingsRequested += OpenSettingsAt;
            _bar.DeadDragNotice += notice => _tray?.ShowNotification("拾语", notice);

            // The header's pin reports only what it wants (票 39/O-20): this
            // side turns it into a one-field update through the store, and
            // the pin's visual state comes back via ApplySettings when the
            // store broadcasts — the bar never writes settings itself again,
            // so its snapshot can no longer erase anyone else's changes (S1/S2).
            _bar.TopmostWanted += wanted =>
                TryUpdateSettings(s => s with { BarAlwaysOnTop = wanted });
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

        // An unchanged geometry — the common case at shutdown — skips the
        // write entirely: every store update re-applies settings everywhere,
        // and exit has no use for that.
        if (Settings.BarLeft == _bar.BarLeft
            && Settings.BarTop == _bar.BarTop
            && Settings.BarHeight == _bar.BarHeight)
        {
            return;
        }

        TryUpdateSettings(s => s with
        {
            BarLeft = _bar.BarLeft,
            BarTop = _bar.BarTop,
            BarHeight = _bar.BarHeight,
        });
    }

    /// <summary>
    /// One badge window, reused. It appears many times an hour; building a
    /// window each time is work the user would feel.
    ///
    /// 复制路径与划词路径共用这一扇窗：划词取词借走的剪贴板作为
    /// <paramref name="selection"/> 随徽标一起挂上——任何一次新 Offer 接手前
    /// 先结算上一笔，徽标换主，旧账要清。
    /// </summary>
    private void ShowBadge(string text, DeferredCapture? selection = null)
    {
        FlushPendingSelection();

        if (_badge is null)
        {
            _badge = new BadgeWindow();
            _badge.Accepted += OnBadgeAccepted;
            _badge.Dismissed += FlushPendingSelection;
        }

        _pendingSelection = selection;
        _badge.Offer(text);
    }

    /// <summary>
    /// 徽标被点击：复制徽标直达面板；划词徽标在面板显示之后再还原剪贴板
    /// （票 37 的 Glossy 次序——还原可以等，点击到出面板这一段不该再添
    /// 一次剪贴板写）。挂起的债在此刻转手给面板，<see cref="_pendingSelection"/>
    /// 清空，避免淡出事件重复还。
    /// </summary>
    private void OnBadgeAccepted(string text)
    {
        var selection = _pendingSelection;
        _pendingSelection = null;

        ShowPanel(text, onDisplayed: selection is null ? null : () => RestoreDeferred(selection));
    }

    /// <summary>Summons the quick bar. One instance, reused: it appears dozens of times
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
    ///
    /// <paramref name="onDisplayed"/> 在面板上屏的那一刻触发；面板出不来时
    /// 立刻触发——挂着不执行的还原就是白借。
    /// </summary>
    private async void ShowPanel(string text, Action? onDisplayed = null)
    {
        if (_hotkeys is null || _writer is null)
        {
            onDisplayed?.Invoke();
            return;
        }

        // async void 里逃出去的异常是进程级崩溃（O-05）；同时面板出不来时
        // 挂着的剪贴板还原就是白借——onDisplayed 用一次性闸门包住，异常
        // 路径也要保证债被还上（且只还一次）。
        var displayed = false;
        void Displayed()
        {
            if (!displayed)
            {
                displayed = true;
                onDisplayed?.Invoke();
            }
        }

        try
        {
            // 朗读服务与面板同寿命：一条专用 STA 线程，懒得起、起一次用到底。
            _speech ??= new SpeechSynthesis();

            _panel ??= new PanelWindow(
                _hotkeys, _writer, () => Settings.BuildTranslationBackend(), Settings,
                SaveTranslationToHistory,
                dictionary: BuildDictionary,
                speech: _speech,

                // 未配置时三条路（复制徽标、划词热键、翻译剪贴板）都落进
                // 面板的引导卡，而不是异常文本（票 08）。
                backendReady: () => Settings.IsTranslationConfigured,
                openSettings: () => OpenSettingsAt("service.preset"));
            await _panel.TranslateAsync(text, Displayed);
        }
        catch (Exception failure)
        {
            // 翻译失败本身已被面板收敛成状态；走到这里的是面板之外的意外。
            Displayed();
            Log.Event(LogEvent.TranslationFailed, failure, ("panel", 1));
            _tray?.ShowNotification("拾语", "翻译面板没能打开，已记录到日志。");
        }
    }

    /// <summary>
    /// 单词词典的路（票 35 + 2026-09-27 回退修正）：英文单词先走免费词典
    /// （600ms 预算），不可达或没查到时**回退 LLM 词典化**（8s 预算，秒级
    /// 迟到也照常上屏）——实测 dictionaryapi.dev 在本机网络完全不可达，无
    /// 回退等于没有卡。中文单词直接 LLM 词典化；其余文本不吃词典卡。
    /// 回退只认自备密钥——公共通道只有 /translate 一张脸；没有自己的后端
    /// 时慢路缺席，行为退回票 35 原状。
    /// </summary>
    private IDictionaryApi? BuildDictionary(string word)
        => DictionaryWord.IsEnglishWord(word)
            ? new FallbackDictionary(
                new BudgetedDictionary(new FreeDictionaryApi()),
                OwnKeyDictionary())
            : DictionaryWord.IsChineseWord(word)
                ? OwnKeyDictionary()
                : null;

    /// <summary>
    /// 自备密钥后端的唯一组装点（票 08）：预设解析出的附加字段与温度规则
    /// 在这里生效——面板的词典、动作、批量翻译都从这一个门进，预设对
    /// 所有模型路径一视同仁。
    /// </summary>
    private OpenAiCompatibleBackend BuildOwnKeyBackend()
    {
        var preset = ProviderPresets.ResolveFor(Settings);
        return new OpenAiCompatibleBackend(
            Settings.Backend,
            extraBody: preset?.ExtraBody,
            maxTemperature: preset?.MaxTemperature,
            sendTemperature: preset?.SendTemperature ?? true);
    }

    /// <summary>
    /// 动作与批量翻译的模型工厂，与面板共用同一条路（票 08）：公共通道
    /// 未上线时它就是自备密钥后端；哪天通道上线而中转还不支持流式对话，
    /// 这里回落自备密钥，而不是把窗口架在一条没有的路上。
    /// </summary>
    private IStreamingModel BuildStreamingModel()
        => Settings.BuildTranslationBackend() as IStreamingModel
            ?? BuildOwnKeyBackend();

    /// <summary>
    /// 自备密钥的 LLM 词典路：有 key 才有路。8s 预算罩住流式取卡——比免费
    /// 路宽一个数量级，因为它是兜底，慢到也仍然胜过没有卡。
    /// </summary>
    private IDictionaryApi? OwnKeyDictionary()
        => string.IsNullOrWhiteSpace(Settings.BackendApiKey)
            ? null
            : new BudgetedDictionary(
                new LlmDictionaryApi(BuildOwnKeyBackend()),
                TimeSpan.FromSeconds(8));

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
            // 动作（总结/合并/改写/建议标签）与批量翻译要的是通用模型，
            // 与面板共用同一个后端工厂（票 08）：预设的附加字段与温度
            // 规则对它们一视同仁。没配密钥时按钮在管理窗侧禁用并说明。
            _library = new LibraryWindow(
                _store, _writer, _images!, BuildStreamingModel, _icons!,
                () => Settings, _pipeline);
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
        // 第一件事就是还债（O-05 修正）：划词借走的剪贴板必须在任何可能
        // 抛出的清理之前归还——原次序里它排在几何保存之后，Save 一抛，
        // 用户就带着我们借走的内容走了。
        FlushPendingSelection();

        // Reverse order of construction: the tray and the clipboard listener
        // both hold the message window.
        _retention?.Stop();
        _theme?.Dispose();
        _settingsWindow?.Close();
        _updateWindow?.Close();
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
        // 两个低级钩子各自住在专用线程上（O-16）：Dispose 投 WM_QUIT、在钩子
        // 线程上卸钩后 Join（带超时），退出路径不会因它们挂住。
        _winV?.Dispose();
        _mouseDrag?.Dispose();
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
        if (IsProbe)
        {
            return;
        }

        if (Settings.TakeOverWinV)
        {
            if (_winV is null)
            {
                try
                {
                    _winV = new WinVHook();
                    _winV.Triggered += ToggleBar;
                }
                catch (Win32Exception failure)
                {
                    // 装钩失败回告（O-16）：字段保持 null——null 就是"未接管"
                    // 的唯一事实，下次进设置或重启会再试。装不上的钩子若装作
                    // 在管，用户要按下 Win+V 看到系统面板弹出那一刻才知道。
                    _tray?.ShowNotification(
                        "拾语", $"未能接管 Win+V：{failure.Message}。本次运行不接管。");
                }
            }
        }
        else if (_winV is not null)
        {
            _winV.Dispose();
            _winV = null;
        }
    }

    /// <summary>
    /// 装上/摘掉划词的鼠标钩子，随设置即时生效——与 Win+V 接管同一个模式。
    /// 默认不装（票 37 的硬约束）：全局低级鼠标钩子让每一次鼠标事件都多绕
    /// 一段本进程，这笔开销只有用户自己点头才花。钩子活在本进程里，关闭
    /// 即刻摘钩，进程退出或被强杀时系统自动还原。
    /// </summary>
    private void ApplySelectionBadge()
    {
        // 同热键：探针不装全局鼠标钩子（票 15）。
        if (IsProbe)
        {
            return;
        }

        if (Settings.SelectionBadge)
        {
            if (_mouseDrag is null)
            {
                try
                {
                    _mouseDrag = new MouseDragHook();
                    _mouseDrag.DragCompleted += OnDragSelected;
                }
                catch (Win32Exception failure)
                {
                    // 同 Win+V：字段保持 null，徽标本次不生效，失败要说出来。
                    _tray?.ShowNotification(
                        "拾语", $"未能开启拖选翻译徽标：{failure.Message}。本次运行不开启。");
                }
            }
        }
        else if (_mouseDrag is not null)
        {
            _mouseDrag.Dispose();
            _mouseDrag = null;
        }
    }

    /// <summary>
    /// 一次拖选完成（UI 线程上）：过滤链前段（总开关 → 桌面早退）拦下不值得
    /// 取词的场合；然后借出剪贴板模拟 Ctrl+C；再由后段（最小长度 → 须含
    /// 字母 → 拒绝路径形）裁决徽标。通过则徽标浮现，借走的剪贴板挂起，等
    /// 面板显示之后或徽标淡出时归还。
    ///
    /// 一切失败都安静收场：拖选是被动遭遇，不是用户点名的动作，安静的
    /// 没有徽标就是全部该有的反馈（划词热键路径保留着它的通知，那是显式
    /// 请求该有的待遇）。
    /// </summary>
    private void OnDragSelected()
    {
        if (_capture is null)
        {
            return;
        }

        // 总开关传实时值：装钩与事件抵达之间设置若被关掉，这里也拦得住。
        if (SelectionBadgeFilter.JudgeBeforeCapture(
                Settings.SelectionBadge, DesktopShell.IsForeground())
            != SelectionBadgeVerdict.Offer)
        {
            return;
        }

        // 排除名单是唯一闸口（ADR-0007），划词也必须过它（O-17）：向密码
        // 管理器模拟 Ctrl+C 去"取选中内容"，取到的就是密码本身。被动遭遇，
        // 安静地不出徽标就是全部该有的反应。
        if (CaptureGate.BlocksForeground(
                _exclusions ?? Settings.BuildExclusionPolicy(), ForegroundApplication.Current().Name))
        {
            return;
        }

        // 新一次取词前先还上一笔——两次快速拖选时，第二笔会借走第一笔的
        // 选中文字，不还就永远找不回用户最初的剪贴板。
        FlushPendingSelection();

        var deferred = _capture.CaptureDeferRestore();
        if (deferred is null)
        {
            return;
        }

        if (deferred.Outcome != CaptureOutcome.Captured
            || SelectionBadgeFilter.JudgeCapturedText(deferred.Text!)
                != SelectionBadgeVerdict.Offer)
        {
            RestoreDeferred(deferred);
            return;
        }

        ShowBadge(deferred.Text!, deferred);
    }

    /// <summary>还掉挂起的划词剪贴板（若有）。幂等：没债就是空操作。</summary>
    private void FlushPendingSelection()
    {
        var pending = _pendingSelection;
        _pendingSelection = null;
        if (pending is not null)
        {
            RestoreDeferred(pending);
        }
    }

    /// <summary>
    /// 归还划词借走的剪贴板。还原失败是唯一值得打断用户的失败——他们的
    /// 剪贴板没了，不说话他们只会从粘贴错东西的那一刻才发现。
    /// </summary>
    private void RestoreDeferred(DeferredCapture deferred)
    {
        if (_capture is not null && !_capture.Restore(deferred))
        {
            Log.Event(LogEvent.ClipboardRestoreFailed);
            _tray?.ShowNotification("拾语", "取词后未能还原你原本的剪贴板内容。");
        }
    }

    private void RegisterHotkeys()
    {
        // A probe owns no global keys (票 15): they belong to the user's real
        // instance, and the guard also covers the re-registration that a
        // settings save would otherwise trigger.
        if (IsProbe)
        {
            Trace.WriteLine("probe mode: hotkeys/hooks off (RegisterHotkeys skipped)");
            return;
        }

        // Ctrl+Shift+Z: deliberately a combination whose modifiers the user is
        // still holding when it fires, so the released-modifier handling in the
        // capture platform is exercised every single time rather than only in
        // some configurations.
        var conflicts = new List<HotkeyConflict>();

        Add(Settings.CaptureHotkey, "划词翻译", TranslateSelection);

        // Ctrl+Shift+V sits next to the paste the user already knows.
        Add(Settings.QuickBarHotkey, "快速条", ShowQuickBar);

        // The resident narrow bar: summoned and hidden by the same key.
        Add(Settings.BarHotkey, "窄条", ToggleBar);

        // The escape hatch. Without it the user cannot tell a filter that
        // judged wrongly from a tool that broke, and has no way to insist.
        Add(Settings.ClipboardTranslateHotkey, "翻译剪贴板内容", TranslateClipboard);

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

        // 显式请求也要过闸口（O-17）：热键可以在任何前台应用按下，包括
        // 排除名单里的。说一声但只说一次——用户多半是忘了规则，每次取词
        // 都弹就成了骚扰。
        if (CaptureGate.BlocksForeground(
                _exclusions ?? Settings.BuildExclusionPolicy(), ForegroundApplication.Current().Name))
        {
            if (!_captureExcludedNotified)
            {
                _captureExcludedNotified = true;
                _tray.ShowNotification("拾语", "前台应用在排除名单里，已跳过取词。");
            }

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
