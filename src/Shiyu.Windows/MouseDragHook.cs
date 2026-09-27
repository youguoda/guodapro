using System.Runtime.InteropServices;
using Shiyu.Core;

namespace Shiyu.Windows;

/// <summary>
/// 划词的拖选观察者（票 37）：WH_MOUSE_LL 低级鼠标钩子。
///
/// 纪律照 <see cref="WinVHook"/>：回调只上报原始事件，一切工作 Post 到装钩
/// 线程做——低级钩子回调里做重活或同步注入输入，系统会摘钩甚至杀进程
/// （票 18 实锤）。这台钩子更进一步，连手势分类都不在回调里做：
/// ≥5px 的位移判定只需要按下与抬起两个端点，所以移动事件——低级钩子里
/// 最高频的流量——在回调里直接丢弃，一根结构体都不 Marshal。
///
/// 钩子是纯观察者：任何事件都放行（CallNextHookEx），从不吞鼠标输入——
/// 拖选检测没有理由让用户的鼠标少动一毫米。
///
/// 与 Win+V 接管同一个道理的保险：钩子活在本进程里，进程退出或被强杀，
/// 系统自动把它摘掉，鼠标行为立刻还原，没有遗留状态可清。
/// </summary>
public sealed class MouseDragHook : IDisposable
{
    private const int WhMouseLl = 14;

    // 低级鼠标钩子的 wParam 是鼠标消息本身。
    private const long WmLeftButtonDown = 0x0201;
    private const long WmLeftButtonUp = 0x0202;
    private const long WmRightButtonDown = 0x0204;
    private const long WmMiddleButtonDown = 0x0207;
    private const long WmXButtonDown = 0x020B;

    private readonly SelectionDrag _drag = new();
    private readonly LowLevelHookProc _proc;
    private readonly IntPtr _hook;
    private readonly SynchronizationContext? _context;

    // 回调里的全部状态就这一位：左键是否按着。它是"哪些事件值得上报"的
    // 门槛，不是手势判定——判定在 UI 线程的 SelectionDrag 里。
    private bool _leftDown;

    private bool _disposed;

    public MouseDragHook()
    {
        _proc = OnHook;
        _context = SynchronizationContext.Current;
        _drag.DragCompleted += () => DragCompleted?.Invoke();

        _hook = NativeMethods.SetWindowsHookExW(
            WhMouseLl, _proc, NativeMethods.GetModuleHandleW(null), 0);
    }

    /// <summary>一次拖选完成（≥5px 位移后左键抬起）。在装钩线程的上下文上触发。</summary>
    public event Action? DragCompleted;

    private IntPtr OnHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            Report(wParam.ToInt64(), lParam);
        }

        // 永远放行：观察者不吞输入。
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void Report(long message, IntPtr lParam)
    {
        switch (message)
        {
            case WmLeftButtonDown:
            case WmLeftButtonUp:
                _leftDown = message == WmLeftButtonDown;
                Post(message, lParam);
                break;

            // 拖选途中混进其它按键会让手势作废——这个信号要上报给状态机；
            // 左键没按着的时候，其它按键与拖选无关，不值得一次 Post。
            case WmRightButtonDown:
            case WmMiddleButtonDown:
            case WmXButtonDown:
                if (_leftDown)
                {
                    Post(message, lParam);
                }

                break;
        }
    }

    /// <summary>把一次原始按钮事件 Post 给 UI 线程上的手势状态机。</summary>
    private void Post(long message, IntPtr lParam)
    {
        var info = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
        var @event = new MouseButtonEvent(
            message == WmLeftButtonDown || message == WmLeftButtonUp
                ? MouseButtonKind.Left
                : MouseButtonKind.Other,
            message == WmLeftButtonDown,
            new ScreenPoint(info.Point.X, info.Point.Y));

        _context?.Post(_ => _drag.Feed(@event), null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public NativeMethods.Point Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
