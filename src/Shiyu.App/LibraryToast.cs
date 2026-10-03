using System.Windows;
using System.Windows.Threading;

namespace Shiyu.App;

/// <summary>
/// 管理窗的反馈（票 24 / UI 报告 §6.4）：撤销条浮在列表左下，40 高，5 秒
/// 进度线，悬停暂停；轻反馈 1.5 秒无进度线；错误走详情栏顶部的 InfoBar
/// （<see cref="ShowError"/>，在 LibraryDetail）。取代被按钮墙盖住的
/// StatusLabel——反馈跟操作发生在同一视野里。
/// </summary>
public partial class LibraryWindow
{
    private static readonly TimeSpan LightFeedbackTime = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan UndoFeedbackTime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ToastTick = TimeSpan.FromMilliseconds(50);

    private DispatcherTimer? _toastTimer;
    private TimeSpan _toastElapsed;
    private TimeSpan _toastTotal;
    private bool _toastIsUndo;

    /// <summary>轻反馈（§6.4）：一句话，1.5 秒，不打断、不留痕。</summary>
    private void ShowLightFeedback(string message)
    {
        ToastText.Text = message;
        ToastUndoButton.Visibility = Visibility.Collapsed;
        ToastProgress.Visibility = Visibility.Collapsed;
        ToastProgress.Width = 0;
        ToastHost.MinWidth = 180;
        StartToast(LightFeedbackTime, isUndo: false);
    }

    /// <summary>撤销条（§6.4）：5 秒进度线，悬停暂停；Z 与「撤销」钮都吃这一窗口。</summary>
    private void ShowUndoFeedback(string message)
    {
        ToastText.Text = message + " — 5 秒内可撤销";
        ToastUndoButton.Visibility = Visibility.Visible;
        ToastProgress.Visibility = Visibility.Visible;
        ToastProgress.Width = 0;
        ToastHost.MinWidth = 240;
        StartToast(UndoFeedbackTime, isUndo: true);
    }

    private void StartToast(TimeSpan total, bool isUndo)
    {
        _toastElapsed = TimeSpan.Zero;
        _toastTotal = total;
        _toastIsUndo = isUndo;

        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = ToastTick };
        _toastTimer.Tick += (_, _) => TickToast();
        _toastTimer.Start();
        ToastHost.Visibility = Visibility.Visible;
    }

    private void TickToast()
    {
        // 悬停暂停：指针在条上时不计时也不走进度线——暂停就是用户在说"等等"。
        if (ToastHost.IsMouseOver)
        {
            return;
        }

        _toastElapsed += ToastTick;
        if (_toastIsUndo)
        {
            ToastProgress.Width = ToastHost.ActualWidth * _toastElapsed.TotalMilliseconds
                / Math.Max(1, _toastTotal.TotalMilliseconds);
        }

        if (_toastElapsed < _toastTotal)
        {
            return;
        }

        // 到期：撤销窗口关闭，留住的原图此刻真走。
        if (_toastIsUndo)
        {
            CommitUndoExpiry();
        }

        HideToast();
    }

    private void HideToast()
    {
        _toastTimer?.Stop();
        _toastTimer = null;
        ToastHost.Visibility = Visibility.Collapsed;
    }
}
