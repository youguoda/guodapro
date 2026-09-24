using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace OverlaySpike;

/// <summary>
/// Spike host for issue 01.
///
/// Run with no arguments for a window you can click around in. Run with
/// <c>--selftest &lt;path&gt;</c> to have it answer the question without a human:
/// it asks Windows itself, via <c>WindowFromPoint</c>, which window would
/// receive a click at chosen points, writes the answers to a file and exits.
/// </summary>
public partial class App : Application
{
    private readonly StringBuilder _log = new();

    private UnderlayWindow? _underlay;
    private OverlayWindow? _overlay;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var selfTestPath = ReadSelfTestPath(e.Args);

        _underlay = new UnderlayWindow { Left = 120, Top = 120 };
        _underlay.Reported = Note;
        _underlay.Show();

        _overlay = new OverlayWindow { Reported = Note };

        // Noted before the overlay appears so the self-test can prove the
        // overlay did not steal it.
        var foregroundBefore = Native.GetForegroundWindow();

        _overlay.Show();

        // After Show, not before: WPF applies its own Left/Top/Width/Height when
        // the window is shown and would overwrite a SetWindowPos made earlier.
        _overlay.CoverScreen();

        _underlay.RowChanged = rect => _overlay.ConnectTo(rect);

        // The row's container does not exist until the list has laid out.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _underlay.PublishSelectedRow();

            if (selfTestPath is null)
            {
                return;
            }

            // Probing cannot follow layout directly. A layered window's hit
            // testing reads the alpha of what has actually been composited to
            // the screen, and UpdateLayout arranges without rendering — so for
            // a moment after the panel moves, the old shape is still what
            // Windows tests against. Waiting for frames to go out is the
            // difference between measuring the panel and measuring a ghost.
            WhenRendered(2, () => RunSelfTest(foregroundBefore, selfTestPath));
        });
    }

    private static string? ReadSelfTestPath(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--selftest")
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private void Note(string message) => _log.AppendLine(message);

    /// <summary>Runs an action after the given number of frames have gone out.</summary>
    private static void WhenRendered(int frames, Action action)
    {
        var remaining = frames;

        void OnRendering(object? sender, EventArgs e)
        {
            if (--remaining > 0)
            {
                return;
            }

            CompositionTarget.Rendering -= OnRendering;
            action();
        }

        CompositionTarget.Rendering += OnRendering;
    }

    private void RunSelfTest(IntPtr foregroundBefore, string path)
    {
        var overlay = new WindowInteropHelper(_overlay!).Handle;
        var underlay = new WindowInteropHelper(_underlay!).Handle;
        var scale = VisualTreeHelper.GetDpi(_overlay!);

        var report = new StringBuilder();

        void Line(string text) => report.AppendLine(text);

        Line("=== 拾语 票 01 · 覆盖层穿透自测 ===");
        Line($"DPI 缩放: {scale.DpiScaleX:0.##}x");
        Line($"工作区: {SystemParameters.WorkArea} DIU");
        Line($"覆盖层 HWND : {Native.Describe(overlay)}");
        Line($"下层窗口 HWND: {Native.Describe(underlay)}");
        Line("");

        Line("--- 是否用到了 WS_EX_TRANSPARENT ---");
        Line($"覆盖层带 WS_EX_TRANSPARENT: {Native.HasTransparentStyle(overlay)}");
        Line("");

        Line("--- 焦点 ---");
        Line($"显示覆盖层之前的前台窗口: {Native.Describe(foregroundBefore)}");
        Line($"显示覆盖层之后的前台窗口: {Native.Describe(Native.GetForegroundWindow())}");
        Line($"覆盖层夺取了前台: {Native.GetForegroundWindow() == overlay}");
        Line("");

        string Which(IntPtr handle) => handle == overlay ? "覆盖层"
            : handle == underlay ? "下层窗口"
            : "其它窗口";

        // Canvas.SetLeft only takes effect on the next layout pass. Measuring
        // before it runs reports the panel still at the canvas origin — which is
        // where three earlier runs of this spike were unknowingly probing.
        _overlay!.UpdateLayout();

        // Asked of WPF, not derived: with monitors at different scale factors
        // the arithmetic is exactly where this goes wrong.
        var centre = _overlay.PanelCentreOnScreen;

        Line("--- 面板位置 ---");
        Line($"覆盖层内部状态: {_overlay.DescribeLayout()}");
        Line($"面板中心（物理像素，由 WPF 给出）: {centre.X:0}, {centre.Y:0}");
        Line($"WPF 自己在该点命中的元素: {_overlay.HitTestInsideOverlay(_overlay.PanelCentreLocal)}");
        Line("");

        var panelCentre = new Native.Point { X = (int)Math.Round(centre.X), Y = (int)Math.Round(centre.Y) };

        // Both asked of WPF in physical pixels, for the same reason as the panel.
        var onUnderlay = _underlay!.PointToScreen(new Point(60, 300));
        var overUnderlay = new Native.Point { X = (int)onUnderlay.X, Y = (int)onUnderlay.Y };

        // A point inside the overlay's bounds but over neither — the curve area.
        var onNothing = _underlay.PointToScreen(new Point(600, 40));
        var overNothing = new Native.Point { X = (int)onNothing.X, Y = (int)onNothing.Y };

        Line("--- 命中测试（问 Windows 自己：这一点的点击会落到哪个窗口）---");
        var atPanel = Native.WindowFromPoint(panelCentre);
        var atUnderlay = Native.WindowFromPoint(overUnderlay);
        var atNothing = Native.WindowFromPoint(overNothing);

        Line($"面板中心         → {Which(atPanel)}   {Native.Describe(atPanel)}");
        Line($"下层窗口上、面板外 → {Which(atUnderlay)}   {Native.Describe(atUnderlay)}");
        Line($"覆盖层内的空白处   → {Which(atNothing)}   {Native.Describe(atNothing)}");
        Line("");

        var panelClickable = atPanel == overlay;
        var passesThrough = atUnderlay == underlay;
        var blankPassesThrough = atNothing != overlay;

        // A locked screen, a screensaver or a full-screen window above us makes
        // every probe return that window instead. Silently reporting "4e0d62107acb"
        // from a contaminated run would be worse than reporting nothing.
        var contaminated = atPanel == atUnderlay && atUnderlay == atNothing && atPanel != overlay;

        Line("--- 结论 ---");
        if (contaminated)
        {
            Line("本轮无效：三个探测点返回同一个外部窗口，说明有全屏窗口/锁屏覆盖在上方。请重跑。");
        }

        Line($"面板可点击              : {(panelClickable ? "是" : "否")}");
        Line($"面板之外穿透到下层应用  : {(passesThrough ? "是" : "否")}");
        Line($"覆盖层空白处不拦截      : {(blankPassesThrough ? "是" : "否")}");
        Line($"整体结论                : {(panelClickable && passesThrough && blankPassesThrough ? "成立" : "不成立")}");

        if (_log.Length > 0)
        {
            Line("");
            Line("--- 交互记录 ---");
            Line(_log.ToString().TrimEnd());
        }

        File.WriteAllText(path, report.ToString(), Encoding.UTF8);
        Shutdown();
    }
}
