using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Shiyu.Windows;

/// <summary>
/// 专用钩子线程（O-16）：一条后台线程装上一个低级钩子，并只为它泵消息。
///
/// 为什么必须是它：低级钩子（WH_KEYBOARD_LL / WH_MOUSE_LL）的回调是系统向
/// 装钩线程发消息来调用的，装钩线程必须泵消息；回调在系统超时（Win10 1709+
/// 上限 1000ms）内得不到执行，事件被放行且钩子被系统**静默摘除**，进程无从
/// 得知——微软文档 LowLevelKeyboardProc/LowLevelMouseProc 的 Remarks 建议的
/// 正是 "run the hooks on a dedicated thread"。拾语的 UI 线程给不出这个承诺：
/// 取词会在它上面 Thread.Sleep 轮询最长 600ms（SelectionCapture），期间钩子
/// 回调得不到执行——开着划词拖动窗口时全系统鼠标跟着卡；开着 Win+V 接管时，
/// 取词注入的 Ctrl+C 在自家钩子上迟到，剪贴板已还原之后才把选区覆盖进去。
///
/// 线程模型（本类无法单测，契约写死在这里）：
/// - 构造在调用线程上同步等待安装结果（ManualResetEventSlim）。
///   SetWindowsHookExW 返回 0 时抛 Win32Exception，钩子线程已自然退出，
///   什么也不遗留——装不上的钩子必须当场可见，不能让设置界面显示
///   "已接管"而系统行为原样（O-16 的另一半）。
/// - 回调在钩子线程上、于 DispatchMessage 内执行——回调读写的任何状态只能
///   活在这条线程上；要通知 UI，用构造时捕获的 SynchronizationContext Post。
///   钩子句柄经回调的第一个参数传入而不是让回调回头读字段：装钩在下面的
///   构造里就已生效，调用方对自己的字段赋值晚于首个回调可能的执行——字段
///   回读会读到 null，而低级钩子回调里抛异常等于泵线程死亡、钩子被系统
///   静默摘除。回调闭包能安全触碰的只有宿主构造前就赋值的调用方字段
///   （字段初始化器与构造函数前段——Thread.Start 建立它们与钩子线程间的
///   happens-before）。
/// - Dispose 可在任意线程调用、幂等：PostThreadMessage(WM_QUIT) 让泵循环
///   退出，钩子由钩子线程自己在循环退出**之后**卸载（装/卸同一条线程，此后
///   回调不可能再执行），Join 带超时兜底——一条被外部挂起的线程不该把进程
///   退出吊住。线程为后台线程，进程退出时即便没人 Dispose，系统也会摘钩。
/// </summary>
internal sealed class LowLevelHookThread : IDisposable
{
    /// <summary>
    /// 钩子回调：第一个参数是宿主装上的钩子句柄（CallNextHookEx 转交用），
    /// 其余与 Win32 的 LowLevelKeyboardProc/LowLevelMouseProc 相同。
    /// </summary>
    public delegate IntPtr Callback(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    private readonly Callback _callback;
    private readonly LowLevelHookProc _dispatch;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _installed = new(false);

    // 三者都由钩子线程在 _installed.Set() 之前写入（Set 的内存屏障负责
    // 发布）：构造线程此后只读一次；_hook 在回调里读时写在同一条线程上，
    // 天然有序。
    private IntPtr _hook;
    private uint _threadId;
    private int _installError;

    private bool _disposed;

    public LowLevelHookThread(int idHook, Callback callback, string threadName)
    {
        _callback = callback;

        // 宿主自己的转发层：_callback 与 _dispatch 都在 Start 之前赋值，
        // 钩子线程经 Thread.Start 的 happens-before 一定看得见它们。
        _dispatch = Dispatch;

        _thread = new Thread(() => Run(idHook))
        {
            IsBackground = true,
            Name = threadName,
        };
        _thread.Start();

        // 等安装结果而不是"线程已跑起来"：调用方拿到的实例要么钩子在位，
        // 要么构造抛异常，不存在第三种"活着但没钩子"的中间态。
        _installed.Wait();
        _installed.Dispose();

        if (_installError != 0)
        {
            throw new Win32Exception(_installError);
        }
    }

    /// <summary>把系统送来的事件转交给调用方回调，钩子句柄随身带走。</summary>
    private IntPtr Dispatch(int code, IntPtr wParam, IntPtr lParam)
        => _callback(_hook, code, wParam, lParam);

    private void Run(int idHook)
    {
        _threadId = NativeMethods.GetCurrentThreadId();

        _hook = NativeMethods.SetWindowsHookExW(
            idHook, _dispatch, NativeMethods.GetModuleHandleW(null), 0);
        if (_hook == IntPtr.Zero)
        {
            // 只带走错误码，异常在调用线程上构造——错误码不会像异常对象
            // 那样带着栈跨线程旅行。
            _installError = Marshal.GetLastWin32Error();
            _installed.Set();
            return;
        }

        _installed.Set();

        // 泵循环：系统对低级钩子的回调经由这里送达。WM_QUIT（或出错返回
        // -1）退出循环；退出即卸钩——先停泵再卸，卸钩之后回调永不复来。
        while (NativeMethods.GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessageW(ref message);
        }

        NativeMethods.UnhookWindowsHookEx(_hook);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 先请循环退出，再等线程：卸钩在钩子线程的循环退出处完成，Join
        // 返回（或超时）才算"钩子已卸"。投递失败只可能是线程已不在——
        // 构造抛过异常的实例根本构造不出来，到不了这里。
        NativeMethods.PostThreadMessageW(
            _threadId, NativeMethods.WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
    }
}
