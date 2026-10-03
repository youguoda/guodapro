using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
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
///
/// 可访问性（票 27 / U-29，票 22 留尾）：ContentControl 的默认 peer 只枚举
/// <see cref="ContentControl.Content"/>，模板里的标题/正文 TextBlock 从不进
/// UIA 树——讲述人只听见一条无名横幅。解法是把「状态 + 标题 + 正文」合成
/// 一句话挂在自己的 <see cref="AutomationProperties.Name"/> 上（标题可读），
/// 并按档位挂 LiveSetting：错误/警示 Assertive，其余 Polite（状态变化会
/// 被朗读）。
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
        new FrameworkPropertyMetadata(InfoBarSeverity.Info, (d, _) => ((InfoBar)d).SyncAutomation()));

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

    /// <summary>
    /// ContentControl 默认不建 peer（票 22 留尾的根因）：整条在 UIA 控件视图
    /// 里不存在，模板 TextBlock 又因 IsControlElement=false 被控件视图排除
    /// ——讲述人两头都读不到错误标题。给一个真正的 peer，条子才在树里，
    /// <see cref="AutomationProperties.Name"/>（合成的状态+标题+正文）与
    /// LiveSetting 才有人递给客户端。
    /// </summary>
    protected override AutomationPeer OnCreateAutomationPeer()
        => new InfoBarAutomationPeer(this);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild(ClosePart) is Button close)
        {
            close.Click += (_, _) => IsOpen = false;
        }

        SyncAutomation();
    }

    private sealed class InfoBarAutomationPeer(InfoBar owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(InfoBar);

        /// <summary>状态条的第一身份是一句会被朗读的文本，不是容器。</summary>
        protected override AutomationControlType GetAutomationControlTypeCore()
            => AutomationControlType.Text;
    }

    private void OnMessageChanged()
    {
        // 空正文收条，与 IsOpen 同一个出口，模板只看一个条件。
        if (string.IsNullOrWhiteSpace(Message))
        {
            IsOpen = false;
        }

        SyncAutomation();
    }

    /// <summary>把状态、标题、正文合成屏幕阅读器念的一句话，并按档位定 LiveSetting。</summary>
    private void SyncAutomation()
    {
        var severity = Severity switch
        {
            InfoBarSeverity.Success => "成功",
            InfoBarSeverity.Caution => "警告",
            InfoBarSeverity.Danger => "错误",
            _ => "提示",
        };

        var title = Title?.Trim() ?? string.Empty;
        var message = Message?.Trim() ?? string.Empty;

        AutomationProperties.SetName(
            this,
            message.Length == 0 ? $"{severity}：{title}" : $"{severity}：{title}。{message}");
        AutomationProperties.SetLiveSetting(
            this,
            Severity is InfoBarSeverity.Danger or InfoBarSeverity.Caution
                ? AutomationLiveSetting.Assertive
                : AutomationLiveSetting.Polite);
    }
}
