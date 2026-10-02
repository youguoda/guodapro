using System.Windows;
using System.Windows.Controls;

namespace Shiyu.App;

/// <summary>InfoBar 的四档语义（票 19 / UI 报告 §4.8）。</summary>
internal enum InfoBarSeverity
{
    /// <summary>中性信息：accent。</summary>
    Info,

    /// <summary>成功反馈（"连接成功"）。</summary>
    Success,

    /// <summary>警示（云同步目录、磁盘余量）——此前误用 Danger 夸大等级。</summary>
    Caution,

    /// <summary>错误。</summary>
    Danger,
}

/// <summary>
/// 状态条（票 19 共享组件）：状态色 10% 底 + 30% 描边 + 状态字形 + 标题/正文，
/// 可带动作（<see cref="ContentControl.Content"/>）与关闭钮。底色与描边画在
/// 两个不承载文字的兄弟 Border 上再用透明度压淡——文字永远全不透明，
/// 对比度测试才管得住它（票 18/R6 的纪律）。
/// </summary>
internal sealed class InfoBar : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(InfoBar), new FrameworkPropertyMetadata(string.Empty));

    /// <summary>标题，BodyStrong。</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(InfoBar),
        new FrameworkPropertyMetadata(string.Empty, (d, _) => ((InfoBar)d).OnMessageChanged()));

    /// <summary>正文，Body。空则整条收起——没有正文的状态条只剩一行喊话。</summary>
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
        nameof(Severity), typeof(InfoBarSeverity), typeof(InfoBar),
        new FrameworkPropertyMetadata(InfoBarSeverity.Info));

    public InfoBarSeverity Severity
    {
        get => (InfoBarSeverity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(InfoBar), new FrameworkPropertyMetadata(true));

    /// <summary>False 即整条收起（关闭钮或代码都走它）。</summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public static readonly DependencyProperty IsClosableProperty = DependencyProperty.Register(
        nameof(IsClosable), typeof(bool), typeof(InfoBar), new FrameworkPropertyMetadata(true));

    public bool IsClosable
    {
        get => (bool)GetValue(IsClosableProperty);
        set => SetValue(IsClosableProperty, value);
    }

    private const string ClosePart = "PART_CloseButton";

    public InfoBar() => SetResourceReference(StyleProperty, "InfoBar");

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild(ClosePart) is Button close)
        {
            close.Click += (_, _) => IsOpen = false;
        }
    }

    private void OnMessageChanged()
    {
        // 空正文收条，与 IsOpen 同一个出口，模板只看一个条件。
        if (string.IsNullOrWhiteSpace(Message))
        {
            IsOpen = false;
        }
    }
}
