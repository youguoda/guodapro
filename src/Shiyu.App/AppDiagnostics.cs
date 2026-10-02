using System.IO;
using System.Windows;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// 进程级兜底（O-05，O-40 拆自 App.xaml.cs）：三个异常处理器、崩溃托盘
/// 提示的节流、启动失败的最后一行落盘。先于一切业务接线挂上——启动路径
/// 上的任何闪失才有日志与托盘兜底，而不是把进程直接带走；对一个托盘
/// 常驻的记录工具，崩溃就等于静默停止记录。
///
/// <see cref="Tell"/> 在托盘起来前是 null：与原字段闭包同一语义，没托盘
/// 可说时就先记在日志里。
/// </summary>
internal sealed class AppDiagnostics
{
    /// <summary>
    /// 崩溃托盘提示的节流表（O-05）：同类异常 5 分钟内只打扰一次。键是
    /// 异常类型+消息的指纹，值是上次提示时间；过期项顺手清，表不设上限
    /// 就成了泄漏。
    /// </summary>
    private readonly Dictionary<string, DateTimeOffset> _crashNotices = new();

    private static readonly TimeSpan CrashNoticeInterval = TimeSpan.FromMinutes(5);

    /// <summary>托盘起来后由宿主接上（AppShell.TellUser）。</summary>
    public Action<string>? Tell { get; set; }

    /// <summary>
    /// 三个进程级兜底。UI 线程的异常记日志、托盘说一次、
    /// <c>Handled=true</c> 挺住继续跑；没人 await 的 Task 记下并认领
    /// （否则进程退出时它们会变成崩溃对话框）；其余线程的致命异常拦是
    /// 拦不住的——处理器返回后 CLR 仍会终止进程，sink 是同步写，这里
    /// 唯一能做的是把现场完整留在盘上再走。
    /// </summary>
    public void Install()
    {
        Application.Current.DispatcherUnhandledException += (_, e) =>
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
        Tell?.Invoke("拾语遇到一个错误，已记录到日志。");
    }

    /// <summary>
    /// Shiyu has no window. Without this, a failure to start is a process that
    /// silently isn't there — nothing to look at, nothing to read. The file is
    /// the only way in.
    /// </summary>
    public static void RecordStartupFailure(Exception exception)
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
}
