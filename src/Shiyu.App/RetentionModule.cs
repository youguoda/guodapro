using Shiyu.Core;

namespace Shiyu.App;

/// <summary>保留清理模块（O-40 拆自 App.xaml.cs）：过期图片原件的即时与每日清扫。</summary>
internal sealed class RetentionModule
{
    private AppShell? _shell;
    private System.Windows.Threading.DispatcherTimer? _timer;

    /// <summary>
    /// Sweeps expired image originals now and once a day thereafter.
    ///
    /// Run on a background thread: a machine left unused for months has a
    /// backlog to work through, and doing it on the thread that draws would
    /// make startup look like a hang.
    /// </summary>
    public void Start(AppShell shell)
    {
        _shell = shell;

        Sweep();

        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromHours(24),
        };
        _timer.Tick += (_, _) => Sweep();
        _timer.Start();
    }

    private void Sweep()
    {
        var shell = _shell!;
        var service = new RetentionService(shell.Store, shell.Images, TimeProvider.System);
        var days = Math.Max(1, shell.Settings.ImageRetentionDays);

        Task.Run(() =>
        {
            try
            {
                var result = service.Sweep(
                    TimeSpan.FromDays(days),
                    shell.Settings.ProtectEntries && shell.Settings.ProtectFavorites,
                    shell.Settings.ProtectEntries && shell.Settings.ProtectPinned);
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

    /// <summary>原 OnExit 的第一步清理：计时器停，不再排队新的清扫。</summary>
    public void Shutdown() => _timer?.Stop();
}
