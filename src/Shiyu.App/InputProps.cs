using System.Windows;

namespace Shiyu.App;

/// <summary>
/// 输入控件的共享附加属性（票 19）。占位符此前有三套各自的浮层实现
/// （窄条搜索框、管理窗搜索框、设置窗搜索框），"值为空即显示"的约定
/// 收进隐式模板里，窗口只声明文案本身，不再各自摆一个叠在上面的
/// TextBlock——三套实现漂移出三种边距，正是评审 §4.8 点名的问题。
/// </summary>
internal static class InputProps
{
    /// <summary>值为空时显示的占位文案；空串即无占位。</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(InputProps),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.Inherits));

    public static string GetPlaceholder(DependencyObject element)
        => (string)element.GetValue(PlaceholderProperty);

    public static void SetPlaceholder(DependencyObject element, string value)
        => element.SetValue(PlaceholderProperty, value);

    /// <summary>
    /// 框内右侧留白：给浮在输入框右缘上的键帽/清除钮让出文字空间——
    /// 命中区不许被裁掉，文字也不许钻到键帽底下。
    /// </summary>
    public static readonly DependencyProperty RightGutterProperty = DependencyProperty.RegisterAttached(
        "RightGutter", typeof(double), typeof(InputProps),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.Inherits));

    public static double GetRightGutter(DependencyObject element)
        => (double)element.GetValue(RightGutterProperty);

    public static void SetRightGutter(DependencyObject element, double value)
        => element.SetValue(RightGutterProperty, value);
}
