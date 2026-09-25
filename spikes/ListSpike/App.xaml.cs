using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ListSpike;

/// <summary>
/// Spike host for issue 02.
///
/// Run with no arguments for a window you can scroll and hover in. Run with
/// <c>--selftest &lt;path&gt;</c> to have it answer what it can without a
/// human: how many containers were built (recycling), whether heights vary,
/// how scrolling performs, whether animation state survives container reuse,
/// and whether the pinned region truly stays put. The feel of scrolling and
/// of the hover animation remains for human eyes.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string? selfTestPath = null;
        var count = 10_000;

        for (var i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--selftest" && i + 1 < e.Args.Length)
            {
                selfTestPath = e.Args[++i];
            }
            else if (e.Args[i] == "--count" && i + 1 < e.Args.Length && int.TryParse(e.Args[++i], out var parsed))
            {
                count = parsed;
            }
        }

        var window = new ListSpikeWindow(count);
        MainWindow = window;

        if (selfTestPath is not null)
        {
            window.Interactive = false;

            // Subscribed before Show: Loaded fires synchronously during the
            // first Show, so a subscription made afterwards never runs.
            window.Loaded += async (_, _) =>
            {
                try
                {
                    // Layout and first scroll positions are settled before probing.
                    await Task.Delay(300);
                    await RunSelfTest(window, count, selfTestPath);
                }
                catch (Exception exception)
                {
                    File.WriteAllText(selfTestPath,
                        "自测中途抛出异常：\n" + exception, Encoding.UTF8);
                }
                finally
                {
                    Shutdown();
                }
            };
        }

        window.Show();
    }

    private static async Task RunSelfTest(ListSpikeWindow window, int count, string path)
    {
        var scroller = window.Scroller ?? throw new InvalidOperationException("找不到滚动视图");
        var process = Process.GetCurrentProcess();
        var report = new StringBuilder();

        void Line(string text) => report.AppendLine(text);

        void Snapshot(string label)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            process.Refresh();

            Line($"{label}: 容器累计创建 {CardContainer.Created} · 当前在树 {window.RealizedContainers().Count} · " +
                 $"ExtentHeight {scroller.ExtentHeight:0} · 工作集 {process.WorkingSet64 / 1024 / 1024} MB · " +
                 $"托管堆 {GC.GetTotalMemory(true) / 1024.0 / 1024.0:0.0} MB");
        }

        Line("=== 拾语 票 02 · 虚拟化列表自测 ===");
        Line($"条目: {count}（置顶 {window.Pinned.Count} · 滚动区 {window.Rest.Count}）");
        Line("");

        await Frames(2);
        var extentStart = scroller.ExtentHeight;
        Snapshot("加载后");

        var heights = window.RealizedHeights();
        Line($"变高行: 在树容器高度 [{string.Join(", ", heights.Select(h => h.ToString("0")))}] · 去重 {heights.Distinct().Count()} 种");
        Line("");

        // --- Scrolling, three ways -----------------------------------------------
        //
        // Wheel-paced: ~120px steps, the size of one mouse-wheel notch. This
        // is the number that answers "is it smooth" — each notch realizes one
        // or two new cards.
        //
        // Burst: the whole range in 48 jumps, forcing synchronous layout per
        // step, no waiting for rendering. Pure layout cost, and a stress test:
        // each jump realizes ~285 cards at once.
        //
        // Framed: the same big jumps, one composition frame per step — how a
        // scrollbar drag thumb-release lands, not how a wheel scrolls.
        var target = scroller.ScrollableHeight;

        scroller.ScrollToVerticalOffset(0);
        await Frames(2);

        const double wheelStep = 120;
        const int wheelSteps = 60;

        // Pure work first, no frame waits: what a notch actually costs in
        // layout. The framed pass below adds vsync/scheduling quantization on
        // top, which is not this spike's question.
        var wheelBurst = Stopwatch.StartNew();
        for (var i = 1; i <= wheelSteps; i++)
        {
            scroller.ScrollToVerticalOffset(wheelStep * i);
            window.MainList.UpdateLayout();
        }
        wheelBurst.Stop();
        Line($"滚轮节奏布局成本: {wheelSteps} 步 × {wheelStep:0}px · 总 {wheelBurst.Elapsed.TotalMilliseconds:0} ms · 每步 {wheelBurst.Elapsed.TotalMilliseconds / wheelSteps:0.00} ms（不含渲染）");

        scroller.ScrollToVerticalOffset(0);
        await Frames(2);

        var worstWheel = 0d;
        var wheelTotal = Stopwatch.StartNew();
        for (var i = 1; i <= wheelSteps; i++)
        {
            var step = Stopwatch.StartNew();
            scroller.ScrollToVerticalOffset(wheelStep * i);
            await Frames(1);
            step.Stop();
            worstWheel = Math.Max(worstWheel, step.Elapsed.TotalMilliseconds);
        }
        wheelTotal.Stop();
        Line($"滚轮节奏滚动: {wheelSteps} 步 × {wheelStep:0}px · 总 {wheelTotal.Elapsed.TotalMilliseconds:0} ms · 最慢单步 {worstWheel:0.0} ms（含等一帧合成，vsync 预算约 16.7 ms）");

        const int steps = 48;

        scroller.ScrollToVerticalOffset(0);
        await Frames(2);

        var burst = Stopwatch.StartNew();
        for (var i = 1; i <= steps; i++)
        {
            scroller.ScrollToVerticalOffset(target * i / steps);
            window.MainList.UpdateLayout();
        }
        burst.Stop();
        Line($"布局突发（极端跳滚）: {steps} 步滚完全程 · 总 {burst.Elapsed.TotalMilliseconds:0} ms · 每步 {burst.Elapsed.TotalMilliseconds / steps:0.00} ms（不含渲染，每步新实现约 {target / steps / 100:0} 张卡）");

        scroller.ScrollToVerticalOffset(0);
        await Frames(2);

        var worstStep = 0d;
        var framed = Stopwatch.StartNew();
        for (var i = 1; i <= steps; i++)
        {
            var step = Stopwatch.StartNew();
            scroller.ScrollToVerticalOffset(target * i / steps);
            await Frames(1);
            step.Stop();
            worstStep = Math.Max(worstStep, step.Elapsed.TotalMilliseconds);
        }
        framed.Stop();
        Line($"分帧跳滚: {steps} 步滚完全程 · 总 {framed.Elapsed.TotalMilliseconds:0} ms · 最慢单步 {worstStep:0.0} ms（含等一帧合成）");
        Line("");

        Snapshot("滚到底后");

        // --- Hover animation vs container recycling -----------------------------
        scroller.ScrollToVerticalOffset(0);
        await Frames(3);

        var container = window.ContainerAt(1)
            ?? throw new InvalidOperationException("索引 1 的容器未实现");
        var tray = Tree.FindDescendant<TrayStrip>(container)
            ?? throw new InvalidOperationException("容器里找不到托盘");

        tray.Open();

        // Wait in composition frames, not wall time: an animation clock only
        // advances with the render clock, and with nothing else dirty on
        // screen a Task.Delay measures a frozen clock as "never opened".
        for (var frame = 0; frame < 16; frame++)
        {
            await Frames(1);
        }

        var opened = tray.ButtonWidths();
        Line($"动画打开: 按钮宽 [{string.Join("/", opened.Select(w => w.ToString("0")))}] · 目标 20 · " +
             $"tray 在树 {Mark(tray.IsLoaded)}");

        // Exactly what recycling does to a container: the same container
        // instance, a new row's data. If Reset works, the tray snaps closed.
        var original = container.DataContext;
        container.DataContext = window.Rest[2];
        var afterSwap = tray.ButtonWidths();
        container.DataContext = original;
        Line($"模拟回收（同容器换 DataContext）: 按钮宽 [{string.Join("/", afterSwap.Select(w => w.ToString("0")))}] · 目标 0");

        // And the real thing: open, scroll the container out of view (it gets
        // recycled), come back, and look at whichever container serves that
        // row now.
        tray.Open();
        await Task.Delay(60);
        scroller.ScrollToVerticalOffset(target);
        await Frames(3);
        scroller.ScrollToVerticalOffset(0);
        await Frames(3);

        var back = window.ContainerAt(1)
            ?? throw new InvalidOperationException("滚回后索引 1 的容器未实现");
        var afterScroll = Tree.FindDescendant<TrayStrip>(back)!.ButtonWidths();
        Line($"真实回收（滚走再滚回 · {(ReferenceEquals(back, container) ? "同一实例被复用" : "换了实例")}）: 按钮宽 [{string.Join("/", afterScroll.Select(w => w.ToString("0")))}] · 目标 0");
        Line("");

        // --- Pinned region -------------------------------------------------------
        var pinnedOutsideScroller = Tree.FindAncestor<ScrollViewer>(window.PinnedList) is null;
        var before = window.PinnedHostControl.PointToScreen(new Point(0, 0));
        scroller.ScrollToVerticalOffset(target / 2);
        await Frames(2);
        var after = window.PinnedHostControl.PointToScreen(new Point(0, 0));
        Line($"置顶区: 独立于 ScrollViewer {Mark(pinnedOutsideScroller)} · 滚动前后屏幕位置不变 {Mark(before == after)}");
        Line("");

        // --- Verdict -------------------------------------------------------------
        var extentEnd = scroller.ExtentHeight;
        var extentDrift = (extentEnd - extentStart) / extentStart * 100.0;
        var containersBounded = CardContainer.Created < count / 10;
        var animationClean = opened.All(w => w > 15)
            && afterSwap.All(w => w < 0.5)
            && afterScroll.All(w => w < 0.5);
        var variableHeights = heights.Distinct().Count() >= 3;
        var pinnedFixed = pinnedOutsideScroller && before == after;

        Line("--- 结论 ---");
        Line($"回收与虚拟化同时成立        : {Mark(containersBounded)}（累计创建 {CardContainer.Created} ≪ 条目 {count}）");
        Line($"变高行                      : {Mark(variableHeights)}");
        Line($"回收不残留动画状态          : {Mark(animationClean)}");
        Line($"置顶固定且不受滚动影响      : {Mark(pinnedFixed)}");
        Line($"滚轮节奏单步（含渲染等待）  : 最慢 {worstWheel:0.0} ms / {wheelSteps} 步（预算 16.7 ms）");
        Line($"ExtentHeight 全程漂移       : {extentDrift:+0.0;-0.0}%（估算随实现修正，thumb 会随之微调）");

        File.WriteAllText(path, report.ToString(), Encoding.UTF8);
    }

    private static string Mark(bool ok) => ok ? "是" : "否";

    /// <summary>Resumes after the given number of composition frames have gone out.</summary>
    private static Task Frames(int count)
    {
        var completion = new TaskCompletionSource();
        var remaining = count;

        void OnRendering(object? sender, EventArgs e)
        {
            if (--remaining > 0)
            {
                return;
            }

            CompositionTarget.Rendering -= OnRendering;
            completion.SetResult();
        }

        CompositionTarget.Rendering += OnRendering;
        return completion.Task;
    }
}
