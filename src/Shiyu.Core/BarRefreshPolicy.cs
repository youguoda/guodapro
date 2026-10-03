namespace Shiyu.Core;

/// <summary>Why the bar hid. Both reasons must run the full teardown.</summary>
public enum BarHideReason
{
    /// <summary>热键开关、Esc 之类：用户亲手收起。</summary>
    Toggled,

    /// <summary>
    /// 从卡片粘贴（票 14 修过的缺陷）：这条路径曾经走 <c>Hide()</c> 捷径，
    /// 计时器不停、也不进轻量模式。原因留在签名里，让"粘贴也走全套"成为
    /// 受测的不变式，而不是又一次靠记得。
    /// </summary>
    Pasted,

    /// <summary>
    /// 粘贴模式失焦即隐（票 26）：用户点了别处。收尾规格与其它原因相同
    /// （轻量开就进轻量）；差别只在调用侧——这条路径只隐藏，不把焦点
    /// 拽回原应用（UI 报告 §3.7 问题 1）。原因单列，为的是这条差别将来
    /// 若要受测，不必回头改调用方的语义。
    /// </summary>
    FocusLost,
}

/// <summary>What the policy wants the window to do after an event.</summary>
[Flags]
public enum BarRefreshCommand
{
    /// <summary>Nothing changed.</summary>
    None = 0,

    /// <summary>重读存储、照现在的样子一次性重建列表。</summary>
    Reload = 1,

    /// <summary>
    /// 进入轻量模式：清掉已实现的卡片、作废在途回填，随后收紧内存
    /// （回收 + 工作集裁剪与卡片清理同生共死，算一条命令）。
    /// </summary>
    EnterLightweight = 2,
}

/// <summary>
/// 窄条的显隐与刷新状态机（O-27 下沉候选 5；票 16）。
///
/// 判定曾经散在三个文件里：<c>Summon</c> 无条件重读、<c>Dismiss</c> 里的
/// 轻量化闸门、<c>OnStoreChanged</c> 的"自己写/不可见就忽略"——隐藏有两条
/// 路径（热键、粘贴），O-37 的计时器泄漏正源于此。这里把仍散在窗口层的
/// 判定收拢为一台纯状态机，App 只剩"把命令应用到窗口"的薄层：
///
/// <list type="bullet">
/// <item><see cref="Shown"/>：隐藏期间忽略的一切一次读齐（照现在的样子，
/// 而不是追认错过的事件）。</item>
/// <item><see cref="Hidden"/>：与原因无关的全套收尾——轻量化开就进轻量
/// （清卡片、作废回填、收紧内存），已隐藏则幂等。</item>
/// <item><see cref="StoreChanged"/>：一条 <c>EntryStore.Changed</c> 恰好
/// 触发一次 <see cref="BarRefreshCommand.Reload"/>——仅当可见且不是本窗
/// 自己的写（自己的写已就地产生过视觉效果，再刷新只会重置滚动）；
/// 隐藏时一律忽略（唤出本来就读新）；滚动加载与后台回填不产生
/// Changed，天然不触发。</item>
/// </list>
///
/// 计时器职责不在此处：票 14 已把预览计时改成
/// <see cref="PreviewPolicy.TimeUntilDecision"/> 的单发限期（可见即闲置的
/// 窄条不再唤醒进程），本策略不再需要 Tick/StartTimers/StopTimers。
/// </summary>
public sealed class BarRefreshPolicy
{
    private bool _visible;
    private bool _lightweightWhenHidden;

    public BarRefreshPolicy(bool lightweightWhenHidden)
    {
        _lightweightWhenHidden = lightweightWhenHidden;
    }

    /// <summary>窗是否处于显示态（策略视角；窗口层照命令执行即与之同步）。</summary>
    public bool IsVisible => _visible;

    /// <summary>设置变更实时生效：轻量化开关只影响此后的隐藏。</summary>
    public void ApplySettings(bool lightweightWhenHidden)
    {
        _lightweightWhenHidden = lightweightWhenHidden;
    }

    /// <summary>唤出：先于一切读一次现状。</summary>
    public BarRefreshCommand Shown()
    {
        _visible = true;
        return BarRefreshCommand.Reload;
    }

    /// <summary>收起：与原因无关的全套收尾；已隐藏则无事可做。</summary>
    public BarRefreshCommand Hidden(BarHideReason reason)
    {
        _ = reason; // 两个原因同待遇——这正是要守住的不变式（见 BarHideReason.Pasted）。
        if (!_visible)
        {
            return BarRefreshCommand.None;
        }

        _visible = false;
        return _lightweightWhenHidden ? BarRefreshCommand.EnterLightweight : BarRefreshCommand.None;
    }

    /// <summary>
    /// 存储变了（一条 Changed 一次调用）：可见且非本窗自己的写才重读，
    /// 其余一律忽略——合并策略见类注释。
    /// </summary>
    public BarRefreshCommand StoreChanged(bool selfWrite)
    {
        if (selfWrite || !_visible)
        {
            return BarRefreshCommand.None;
        }

        return BarRefreshCommand.Reload;
    }
}
