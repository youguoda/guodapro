using System.Windows;
using System.Windows.Controls;

namespace Shiyu.App;

/// <summary>
/// 空状态（票 19 共享组件）：48 的状态字形、Subtitle 标题、Body 说明、
/// 可选动作（<see cref="ContentControl.Content"/>）。"没有结果"读作坏掉，
/// 恰是用户最需要指路的时刻——所以空态是组件不是随手摆的两行字。
/// </summary>
internal sealed class EmptyState : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyState), new FrameworkPropertyMetadata(string.Empty));

    /// <summary>标题，Type.Subtitle。</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(EmptyState), new FrameworkPropertyMetadata(string.Empty));

    /// <summary>说明，Body 的次级色。</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(EmptyState),
        new FrameworkPropertyMetadata("\uE89F"));

    /// <summary>Segoe Fluent 字形码位；默认 E89F（清单图标）。</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public EmptyState() => SetResourceReference(StyleProperty, "EmptyState");
}
