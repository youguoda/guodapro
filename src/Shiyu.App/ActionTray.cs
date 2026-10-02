using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// The hover tray on a card（§6.1 托盘）: a h32 strip floating ON the meta row,
/// not inside it — 32×32 hits on a 32 pitch, 16 DIP glyphs resting in
/// TextSecondary (delete keeps its danger red at rest, per the state matrix),
/// a 1×16 divider before the delete button, and the open/locate pair folding
/// into ⋯ once the tray would pass eight buttons. Because the tray is an
/// overlay, the card's height is a constant: the strip slides in over the
/// timestamp (which hands over by fading) on the motion system's fast tier.
///
/// Built per card, because the action set differs per entry kind — and by
/// entry state: the favourite and pin buttons carry the glyph of the state
/// the entry is IN, not a fixed symbol. All motion lives in code rather than
/// style storyboards on purpose: with container recycling the same tray
/// instance moves to another row's card, and only code can tear the previous
/// row's animation state down at that moment (<see cref="Reset"/> — the
/// issue 02 spike's finding).
/// </summary>
internal sealed class ActionTray : Grid
{
    private const int ButtonSize = 32;

    /// <summary>Past eight buttons the low-frequency pair folds into ⋯ (§6.1).</summary>
    private const int MaxButtons = 8;

    private const string MoreId = "more";

    /// <summary>The folded pair, in tray order; emptied unless the tray overflows.</summary>
    private IReadOnlyList<string> _folded = [];

    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal };

    private BarCard? _card;

    public ActionTray()
    {
        VerticalAlignment = VerticalAlignment.Top;
        HorizontalAlignment = HorizontalAlignment.Right;
        Margin = new Thickness(0, 0, 12, 0);
        RenderTransform = new TranslateTransform();

        // Rests closed and untouchable; Open() is the only way in.
        Opacity = 0;
        IsHitTestVisible = false;

        Children.Add(Plate());
        Children.Add(_buttons);
    }

    /// <summary>Raises an executed action. Set by the window.</summary>
    public event Action<string, BarCard, Button>? ActionExecuted;

    /// <summary>
    /// The ⋯ button's ask: run the folded actions. The window owns the popup
    /// (it has the menu chrome); the tray only knows where it sits.
    /// </summary>
    public event Action<BarCard, IReadOnlyList<string>, Button>? MoreRequested;

    /// <summary>Rebuilds the buttons for the card this tray now belongs to.</summary>
    public void Configure(BarCard card, IReadOnlyList<string> actions)
    {
        _card = card;
        _buttons.Children.Clear();

        // 超过 8 枚收 ⋯：打开/定位入内，删除恒在可见面上（§6.1）。
        var fold = actions.Count > MaxButtons;
        _folded = fold ? actions.Where(id => id is "open" or "locate").ToArray() : [];
        var folding = _folded.Count > 0;

        var visible = actions.Where(id => !_folded.Contains(id)).ToList();
        var moreAt = folding ? actions.Count - visible.Count : -1;

        for (var i = 0; i < visible.Count; i++)
        {
            if (folding && i == moreAt)
            {
                _buttons.Children.Add(MakeButton(MoreId, card));
            }

            // The divider sits before delete wherever delete travels — the
            // destructive act is separated from the rest of the row.
            if (visible[i] == "delete")
            {
                _buttons.Children.Add(Divider());
            }

            _buttons.Children.Add(MakeButton(visible[i], card));
        }
    }

    private static Border Divider()
    {
        var divider = new Border
        {
            Width = 1,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0),
        };
        divider.SetResourceReference(Border.BackgroundProperty, "Brush.Divider");
        return divider;
    }

    /// <summary>
    /// The tray's backing plate: the card's surface colour, fading in over 16
    /// DIP at the left edge so the glyphs read over whatever they float on.
    /// The mask is geometry (fixed stops) so the colour itself stays a live
    /// resource — theme changes keep reaching it.
    /// </summary>
    private static Border Plate()
    {
        var plate = new Border
        {
            Margin = new Thickness(-16, 0, 0, 0),
            OpacityMask = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(16, 0),
                GradientStops =
                {
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(Colors.Black, 1),
                },
            },
        };
        plate.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        return plate;
    }

    /// <summary>
    /// The glyph a button carries for this card right now: favourite and pin
    /// follow the entry's state (空心星 = 未收藏), everything else its action
    /// glyph; ⋯ collects the folded pair.
    /// </summary>
    private static string GlyphFor(string id, BarCard card) => id switch
    {
        "favorite" => card.Favorite ? "\uE735" : "\uE734",
        "pin" => card.IsPinned ? "\uE77A" : "\uE718",
        MoreId => "\uE710",
        _ => HoverActions.IconGlyph(id),
    };

    private Button MakeButton(string id, BarCard card)
    {
        var key = BarKeys.TrayKey(id);
        var danger = HoverActions.IsDestructive(id);

        // The symbol is a TextBlock we own, not a bare string: a string would
        // be presented through a TextBlock that the implicit TextBlock style
        // forces onto Font.Ui, and symbol codepoints render as tofu boxes.
        var label = new TextBlock
        {
            Text = GlyphFor(id, card),
            FontFamily = IconFont,
        };
        label.SetResourceReference(TextElement.FontSizeProperty, "Size.IconS");

        var button = new Button
        {
            Width = ButtonSize,
            Height = ButtonSize,
            Focusable = false,
            Tag = new TraySlot(id, GlyphFor(id, card)),
            Content = label,
            Cursor = Cursors.Hand,
            ToolTip = id == MoreId
                ? $"更多（{string.Join(" / ", _folded.Select(HoverActions.Name))}）"
                : key is null
                    ? $"{HoverActions.Name(id)}（{HoverActions.Glyph(id)}）"
                    : $"{HoverActions.Name(id)}（{HoverActions.Glyph(id)} · {key}）",
            Style = (Style)TryFindResource(danger ? "TrayDangerButtonStyle" : "TrayButtonStyle")
                ?? new Style(typeof(Button)),
        };

        // Every mouse event stops here — on the bubbling versions, after the
        // button's own class handlers have run, so the button still clicks
        // while the press never reaches the card underneath. Swallowing the
        // preview events instead kills the button's Click mechanism outright,
        // which is how the tray first shipped: visible, Pressable, dead.
        button.MouseLeftButtonDown += Swallow;
        button.MouseLeftButtonUp += Swallow;
        button.MouseRightButtonDown += Swallow;
        button.MouseRightButtonUp += Swallow;
        button.MouseDown += Swallow;
        button.MouseUp += Swallow;

        button.Click += (_, _) =>
        {
            if (id == MoreId)
            {
                MoreRequested?.Invoke(card, _folded, button);
                return;
            }

            ActionExecuted?.Invoke(id, card, button);
        };

        return button;

        static void Swallow(object sender, MouseButtonEventArgs e) => e.Handled = true;
    }

    /// <summary>One button's identity: its action id and the state glyph it carries.</summary>
    private sealed record TraySlot(string Id, string Glyph);

    /// <summary>
    /// While Ctrl is held, buttons that answer to a letter show the letter
    /// instead of their glyph — the badge, the tooltip and the key handler all
    /// read <see cref="KeyMap"/> through <see cref="BarKeys"/>, so they cannot
    /// disagree. Letters are UI-font text, not symbol-font codepoints, so the
    /// family swaps with the content. Buttons without a key (and ⋯) keep their
    /// glyph.
    /// </summary>
    public void ShowHints(bool on)
    {
        foreach (var button in _buttons.Children.OfType<Button>())
        {
            if (button.Tag is not TraySlot slot || button.Content is not TextBlock label)
            {
                continue;
            }

            var key = BarKeys.TrayKey(slot.Id);
            var letter = on && key is not null;
            label.Text = letter ? key : slot.Glyph;
            label.FontFamily = letter ? UiFont : IconFont;
        }
    }

    private static FontFamily? IconFont => Application.Current.TryFindResource("Font.Icon") as FontFamily;

    private static FontFamily? UiFont => Application.Current.TryFindResource("Font.Ui") as FontFamily;

    public void Open() => Animate(open: true);

    public void Close() => Animate(open: false);

    /// <summary>
    /// Called when the owning container is recycled onto another row: any
    /// in-flight or held animation from the previous row is removed and the
    /// tray snaps to its closed base state, so the new row starts clean.
    /// </summary>
    public void Reset()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        IsHitTestVisible = false;

        if (RenderTransform is TranslateTransform slide)
        {
            slide.BeginAnimation(TranslateTransform.XProperty, null);
            slide.X = 0;
        }
    }

    private void Animate(bool open)
    {
        // Open first, then move: the buttons become touchable the moment the
        // tray starts arriving, and stop being touchable the moment it leaves.
        IsHitTestVisible = open;
        BeginAnimation(OpacityProperty, Motion.Fade(open ? 1 : 0));

        if (RenderTransform is TranslateTransform slide)
        {
            slide.BeginAnimation(TranslateTransform.XProperty, Motion.Double(open ? 0 : 8));
        }
    }
}
