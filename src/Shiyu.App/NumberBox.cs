using System.Windows;
using System.Windows.Controls;

namespace Shiyu.App;

/// <summary>
/// 数值输入（票 19 共享组件）：值左对齐，单位后缀画在框内右侧的次级色；
/// Enter 或失焦才提交，非法输入保持旧值——把"正在输入"和"已经认可"
/// 分开，正是设置里保留天数/行数/延迟这类数值项需要的语义。
/// </summary>
internal sealed class NumberBox : TextBox
{
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(NumberBox), new FrameworkPropertyMetadata(string.Empty));

    /// <summary>单位后缀（如"天""毫秒"），空串则不画。</summary>
    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public static readonly RoutedEvent CommittedEvent = EventManager.RegisterRoutedEvent(
        nameof(Committed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(NumberBox));

    /// <summary>一次成功的提交（Enter/失焦且值合法）。新值读 <see cref="Text"/>。</summary>
    public event RoutedEventHandler Committed
    {
        add => AddHandler(CommittedEvent, value);
        remove => RemoveHandler(CommittedEvent, value);
    }

    /// <summary>最后一次合法的文本；非法失焦回到它。</summary>
    private string _committed = string.Empty;

    public NumberBox()
    {
        // 键控样式而非隐式：TextBox 派生类按类型找隐式样式只认 TextBox 本身，
        // 这里从应用级字典按名取，换主题时随字典热换。
        SetResourceReference(StyleProperty, "NumberBox");
        LostFocus += (_, _) => Commit();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };
    }

    /// <summary>
    /// 覆盖起点：外部灌进来的初值就是第一个"已认可"值，否则未编辑就失焦
    /// 会被当成非法而清空。
    /// </summary>
    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);
        if (!IsKeyboardFocusWithin)
        {
            _committed = Text;
        }
    }

    private void Commit()
    {
        if (double.TryParse(Text.Trim(), System.Globalization.CultureInfo.CurrentCulture, out _))
        {
            _committed = Text.Trim();
            Text = _committed;
            RaiseEvent(new RoutedEventArgs(CommittedEvent, this));
            return;
        }

        // 非法不是错误态，是"没发生"：回到上一个被认可的值。
        Text = _committed;
    }
}
