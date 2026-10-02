namespace Shiyu.Core;

/// <summary>
/// 一次转移后该对剪贴板做的事（O-27 下沉候选 2）：按给定次序把每笔债交还
/// 给 <see cref="SelectionCapture.Restore"/>；空表即无动作。没有"还原失败"
/// 这一档——还原成败由执行方（App）转告用户，账本只管"还或还没还"。
/// </summary>
public sealed record SelectionDebtAction(IReadOnlyList<DeferredCapture> Restore)
{
    public static SelectionDebtAction None { get; } = new([]);
}

/// <summary>
/// 划词"借出—还原"的账本（O-27 下沉候选 2）。全应用最硬的承诺——无论
/// 发生什么，用户原来的剪贴板都会被还原——此前散在 App 的五处接线里：
/// 新徽标顶替旧债、淡出、点击、面板显示（含面板打不开）、退出，全部零
/// 测试。现在它是 Core 里的一台状态机，每个转移返回应执行的还原，结构
/// 上保证每笔债<b>恰好结算一次</b>：
///
/// <list type="bullet">
/// <item>债一次只在一个人手里——挂在徽标上，或（点击后）转手给正在上屏
/// 的面板；<see cref="Offer"/> 顶替旧债时先还旧，<see cref="Exit"/> 把
/// 所有在册的债一次结清。</item>
/// <item>"结算"是否真的写剪贴板由 <see cref="SelectionCapture.Restore"/>
/// 的守卫决定：借出之后剪贴板若已被用户自己更新过，还原会踩掉更新的
/// 内容——那比不还更糟，这笔债就地销账。账本保证的是每笔债都被交去
/// 结算，且只交这一次。</item>
/// </list>
///
/// 面板侧的债是先进先出的队列：面板是同一扇复用窗，上屏有先后，晚到的
/// <see cref="PanelDisplayed"/> 结算最早的那笔。
/// </summary>
public sealed class SelectionDebt
{
    /// <summary>当前徽标挂着的债（复制徽标为 null）。</summary>
    private DeferredCapture? _heldByBadge;

    /// <summary>已点击、正在等面板上屏的债，先进先出。</summary>
    private readonly Queue<DeferredCapture> _heldByPanel = [];

    /// <summary>当前徽标挂着的债；复制徽标路径为 null。</summary>
    public DeferredCapture? HeldByBadge => _heldByBadge;

    /// <summary>
    /// 新徽标带来了新借的一笔（划词路径）。旧徽标若还有挂着的债，先还——
    /// 徽标换主，旧账要清；已转手给面板的旧债不动，它听面板的。
    /// </summary>
    public SelectionDebtAction Offer(DeferredCapture debt)
    {
        var restore = SettleBadge();
        _heldByBadge = debt;
        return restore;
    }

    /// <summary>
    /// 徽标被点击：债转手给正在打开的面板，还原推迟到面板上屏（票 37 的
    /// Glossy 次序）。复制徽标点击（本无债）是空操作。
    /// </summary>
    public SelectionDebtAction Accepted()
    {
        if (_heldByBadge is { } debt)
        {
            _heldByBadge = null;
            _heldByPanel.Enqueue(debt);
        }

        return SelectionDebtAction.None;
    }

    /// <summary>
    /// 面板上屏了（或确定出不来——App 在两条路径上都调这里，面板失败时
    /// 挂着不执行的还原就是白借）：结算最早转手的那笔。重复调用在队空后
    /// 自然为空操作。
    /// </summary>
    public SelectionDebtAction PanelDisplayed()
        => _heldByPanel.Count > 0
            ? new SelectionDebtAction([_heldByPanel.Dequeue()])
            : SelectionDebtAction.None;

    /// <summary>
    /// 徽标未被点击就淡出（也用于复制徽标顶替、新一次取词前的清账）：
    /// 结算徽标手里那笔。只动徽标——已转手给面板的债不会被一次迟到的
    /// 淡出事件重复还。
    /// </summary>
    public SelectionDebtAction Dismissed() => SettleBadge();

    /// <summary>
    /// 退出：把在册的债全部结清，新债在前、旧债在后——最后落板的是最早
    /// 借走的那份，即用户动手取词之前的原文。
    /// </summary>
    public SelectionDebtAction Exit()
    {
        var all = new List<DeferredCapture>();
        if (_heldByBadge is { } badge)
        {
            _heldByBadge = null;
            all.Add(badge);
        }

        // 队列先进先出存的是"旧→新"，倒过来还，最旧的压轴。
        all.AddRange(_heldByPanel.Reverse());
        _heldByPanel.Clear();
        return new SelectionDebtAction(all);
    }

    private SelectionDebtAction SettleBadge()
    {
        if (_heldByBadge is not { } debt)
        {
            return SelectionDebtAction.None;
        }

        _heldByBadge = null;
        return new SelectionDebtAction([debt]);
    }
}
