namespace Shiyu.Core;

/// <summary>
/// 拖选手势里值得分类的按钮。左键是唯一的拖选键；其余按钮只有一个语义——
/// 污染进行中的手势，让它作废。
/// </summary>
public enum MouseButtonKind
{
    Left,

    /// <summary>左键以外的任何按钮按下：取消进行中的手势。</summary>
    Other,
}

/// <summary>一次原始鼠标按钮事件，坐标为物理屏幕像素。</summary>
public readonly record struct MouseButtonEvent(
    MouseButtonKind Button, bool Down, ScreenPoint Position);

/// <summary>
/// 拖选手势分类（票 37）：把原始按钮事件流归类成"这是不是一次拖选"。
///
/// 判定只需要端点——左键按下与抬起两点的直线位移 ≥5px 即拖选，所以移动
/// 事件根本不进这台状态机（钩子里最高频的流量在更外层就被丢弃了）。
/// 拖选永不延迟：手势在左键抬起的瞬间判定，没有去抖、没有等待。
///
/// 纯逻辑、可注入事件流：真实事件由 WH_MOUSE_LL 钩子 Post 上来，这台
/// 机器永远跑在 UI 线程上（纪律照 WinVFilter/WinVHook 的分工）。
/// </summary>
public sealed class SelectionDrag
{
    /// <summary>
    /// 拖选判定的位移门槛。小于它的按下-抬起是点击或手抖——为每一次点击
    /// 弹翻译徽标是打扰，不是服务。
    /// </summary>
    public const int ThresholdPixels = 5;

    private ScreenPoint _start;
    private bool _tracking;
    private bool _cancelled;

    /// <summary>一次拖选完成（左键抬起且位移达标）。在喂入线程上触发。</summary>
    public event Action? DragCompleted;

    public void Feed(MouseButtonEvent button)
    {
        switch (button.Button, button.Down)
        {
            case (MouseButtonKind.Left, true):
                // 双按下（注入序列里才见得到）以新起点重开手势，不累积旧位移。
                _start = button.Position;
                _tracking = true;
                _cancelled = false;
                break;

            case (MouseButtonKind.Other, true) when _tracking:
                // 拖选中混入其它按键：手势作废，直到下一次左键按下。
                _cancelled = true;
                break;

            case (MouseButtonKind.Left, false) when _tracking:
                _tracking = false;
                if (!_cancelled && DisplacementAtLeast(button.Position))
                {
                    DragCompleted?.Invoke();
                }

                break;
        }
    }

    /// <summary>直线距离平方与门槛平方比——免开方，也不引入浮点。</summary>
    private bool DisplacementAtLeast(ScreenPoint end)
    {
        var dx = end.X - _start.X;
        var dy = end.Y - _start.Y;
        return dx * dx + dy * dy >= ThresholdPixels * ThresholdPixels;
    }
}
