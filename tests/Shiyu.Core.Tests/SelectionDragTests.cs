using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// 拖选手势的分类（票 37）：只有"左键按下 → 移开 ≥5px → 左键抬起"算一次
/// 拖选。点击、纯数字位移不足的抖动、被其它按键污染的手势都不算。
/// 位移按起止两点的直线距离计——拖选的判定只需要端点，移动事件不进状态机。
/// </summary>
public class SelectionDragTests
{
    private static void Press(SelectionDrag drag, int x, int y)
        => drag.Feed(new MouseButtonEvent(MouseButtonKind.Left, true, new ScreenPoint(x, y)));

    private static void Release(SelectionDrag drag, int x, int y)
        => drag.Feed(new MouseButtonEvent(MouseButtonKind.Left, false, new ScreenPoint(x, y)));

    private static int Drags(params MouseButtonEvent[] events)
    {
        var drag = new SelectionDrag();
        var fired = 0;
        drag.DragCompleted += () => fired++;
        foreach (var eventsItem in events)
        {
            drag.Feed(eventsItem);
        }

        return fired;
    }

    [Fact]
    public void A_left_drag_of_five_pixels_completes_the_gesture()
    {
        var drag = new SelectionDrag();
        var fired = 0;
        drag.DragCompleted += () => fired++;

        Press(drag, 100, 100);
        Assert.Equal(0, fired); // 手势在抬起时才判定，拖动途中永不触发
        Release(drag, 105, 100);

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Displacement_counts_along_a_diagonal_not_per_axis()
    {
        // 3-4-5：每个轴都不到 5px，直线距离恰好 5px。
        var drag = new SelectionDrag();
        var fired = 0;
        drag.DragCompleted += () => fired++;

        Press(drag, 0, 0);
        Release(drag, 3, 4);

        Assert.Equal(1, fired);
    }

    [Fact]
    public void Four_pixels_is_a_click_jitter_not_a_drag()
    {
        var drag = new SelectionDrag();
        var fired = 0;
        drag.DragCompleted += () => fired++;

        Press(drag, 100, 100);
        Release(drag, 103, 101);

        Assert.Equal(0, fired);
    }

    [Fact]
    public void A_plain_click_never_completes_a_gesture()
        => Assert.Equal(0, Drags(
            new MouseButtonEvent(MouseButtonKind.Left, true, new ScreenPoint(10, 10)),
            new MouseButtonEvent(MouseButtonKind.Left, false, new ScreenPoint(10, 10))));

    [Fact]
    public void Another_button_pressing_mid_drag_cancels_the_gesture()
    {
        var drag = new SelectionDrag();
        var fired = 0;
        drag.DragCompleted += () => fired++;

        Press(drag, 0, 0);
        drag.Feed(new MouseButtonEvent(MouseButtonKind.Other, true, new ScreenPoint(2, 2)));
        Release(drag, 40, 0);

        Assert.Equal(0, fired);
    }

    [Fact]
    public void The_gesture_after_a_cancelled_one_works_again()
    {
        var drag = new SelectionDrag();
        var fired = 0;
        drag.DragCompleted += () => fired++;

        Press(drag, 0, 0);
        drag.Feed(new MouseButtonEvent(MouseButtonKind.Other, true, new ScreenPoint(2, 2)));
        Release(drag, 40, 0);

        Press(drag, 10, 10);
        Release(drag, 10, 30);

        Assert.Equal(1, fired);
    }

    [Fact]
    public void A_left_release_without_a_press_is_ignored()
        => Assert.Equal(0, Drags(
            new MouseButtonEvent(MouseButtonKind.Left, false, new ScreenPoint(500, 500))));

    [Fact]
    public void A_second_press_mid_drag_restarts_from_the_new_point()
    {
        var drag = new SelectionDrag();
        var fired = 0;
        drag.DragCompleted += () => fired++;

        Press(drag, 0, 0);
        Press(drag, 100, 0); // 注入序列里才会出现的双按下：以新起点重开手势
        Release(drag, 103, 0);

        Assert.Equal(0, fired);
        Release(drag, 106, 0);
        Assert.Equal(0, fired); // 抬起之后没有手势在途中

        Press(drag, 0, 0);
        Release(drag, 100, 0);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void A_drag_back_to_the_starting_point_is_not_a_selection()
    {
        // 起点画圈回原点：净位移为零，是抖动或无效手势，不是拖选。
        var drag = new SelectionDrag();
        var fired = 0;
        drag.DragCompleted += () => fired++;

        Press(drag, 50, 50);
        Release(drag, 50, 50);

        Assert.Equal(0, fired);
    }

    [Fact]
    public void The_threshold_is_measured_from_the_press_point_not_the_release_path()
    {
        // 只看端点：中途绕多远与判定无关（移动事件根本不进状态机）。
        var drag = new SelectionDrag();
        var fired = 0;
        drag.DragCompleted += () => fired++;

        Press(drag, 0, 0);
        Release(drag, 0, 5);

        Assert.Equal(1, fired);
    }
}
