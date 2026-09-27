using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>One item's edited value, whatever its control shape.</summary>
internal sealed class ItemState
{
    public string Text = string.Empty;
    public int Choice;
    public bool Toggle;
}

/// <summary>
/// Control builders shared by the settings window and the first-run guide.
/// Both surfaces render the same data tree; these builders are the single
/// place that turns a declared control type into widgets, so a toggle built
/// for onboarding is pixel-for-pixel the toggle built for settings — there
/// is no second set of definitions to drift.
/// </summary>
internal static class ItemEditors
{
    public static FrameworkElement Text(SettingsItem item, AppSettings current, ItemState state)
    {
        var box = new TextBox
        {
            Text = SettingsBindings.ReadText(item.Id, current) ?? string.Empty,
            Padding = new Thickness(4),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        box.SetResourceReference(TextBox.BackgroundProperty, "Brush.SurfaceInput");
        state.Text = box.Text;
        box.TextChanged += (_, _) => state.Text = box.Text;

        if (item.Control is SettingsControl.Multiline)
        {
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
            box.Height = 110;
            box.VerticalContentAlignment = VerticalAlignment.Top;
        }

        return Wrap(box, item.Hint);
    }

    /// <param name="changed">Raised on picks; settings uses it to collapse dependent rows.</param>
    /// <param name="initialChoice">
    /// 覆盖初选（如引导把未配置用户直接落在公共通道）；省略时读当前设置。
    /// </param>
    public static FrameworkElement Segmented(
        SettingsItem item,
        AppSettings current,
        ItemState state,
        Action<int>? changed = null,
        int? initialChoice = null)
    {
        state.Choice = initialChoice ?? SettingsBindings.ReadChoice(item.Id, current);

        var host = new StackPanel { Orientation = Orientation.Horizontal };
        ToggleButton[] buttons = new ToggleButton[item.ChoiceList.Length];

        for (var index = 0; index < item.ChoiceList.Length; index++)
        {
            var captured = index;
            var button = new ToggleButton
            {
                Content = item.ChoiceList[index],
                Padding = new Thickness(12, 3, 12, 3),
                Cursor = Cursors.Hand,

                // Checked/hover/pressed states live in the shared style; the
                // local value here would override the style's triggers.
                Style = (Style)Application.Current.FindResource("SegmentChip"),
            };
            button.Click += (_, _) =>
            {
                state.Choice = captured;
                foreach (var other in buttons)
                {
                    other.IsChecked = ReferenceEquals(other, button);
                }

                changed?.Invoke(captured);
            };
            buttons[index] = button;
            host.Children.Add(button);
        }

        Paint();
        return host;

        // The resting state shows what is chosen; every click repaints its own
        // row so callers never need to track buttons.
        void Paint()
        {
            foreach (var (button, index) in buttons.Select((button, index) => (button, index)))
            {
                button.IsChecked = index == state.Choice;
            }
        }
    }

    /// <param name="changed">Raised on flips; settings uses it to collapse child rows.</param>
    public static FrameworkElement Toggle(
        SettingsItem item, AppSettings current, ItemState state, Action? changed = null)
    {
        state.Toggle = item.Id == "store.start-with-windows"

            // Read from Windows rather than the settings file: the two can
            // disagree, and what Windows actually does is the truth.
            ? StartupRegistration.IsEnabled()
            : SettingsBindings.ReadToggle(item.Id, current) == true;

        // The save path reads Text ("1"/"0"), so it must be true from the
        // very first moment: an untouched toggle saving as "off" silently
        // zeroes settings the user never touched.
        state.Text = state.Toggle ? "1" : "0";

        // The checkbox carries no text: the row label already names the item,
        // and a long hint as checkbox content truncates mid-sentence. The
        // hint reads beneath, full width.
        var box = new CheckBox
        {
            IsChecked = state.Toggle,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
        };
        box.Checked += (_, _) =>
        {
            state.Toggle = true;
            state.Text = "1";
            changed?.Invoke();
        };
        box.Unchecked += (_, _) =>
        {
            state.Toggle = false;
            state.Text = "0";
            changed?.Invoke();
        };

        if (item.Hint is not { Length: > 0 })
        {
            return box;
        }

        var stack = new StackPanel();
        stack.Children.Add(box);
        var hint = new TextBlock
        {
            Text = item.Hint,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Opacity = 0.75,
            Margin = new Thickness(0, 2, 0, 0),
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        stack.Children.Add(hint);
        return stack;
    }

    /// <summary>Returns the editor and the box, so callers can clear it after a save.</summary>
    public static (FrameworkElement Editor, PasswordBox Box) Password(
        SettingsItem item, AppSettings current, ItemState state)
    {
        // A credential is never echoed: the box starts empty whatever the
        // store holds, and blank means "keep".
        var box = new PasswordBox { Padding = new Thickness(4) };
        box.SetResourceReference(PasswordBox.BackgroundProperty, "Brush.SurfaceInput");
        box.PasswordChanged += (_, _) => state.Text = box.Password;

        return (Wrap(box, item.Hint), box);
    }

    private static FrameworkElement Wrap(FrameworkElement editor, string? hint)
    {
        if (hint is not { Length: > 0 })
        {
            return editor;
        }

        var stack = new StackPanel();
        editor.Margin = new Thickness(0, 0, 0, 2);
        stack.Children.Add(editor);

        var text = new TextBlock
        {
            Text = hint,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Opacity = 0.75,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        stack.Children.Add(text);
        return stack;
    }
}
