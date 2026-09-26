using System.Windows;
using System.Windows.Controls;
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

        var box = new CheckBox
        {
            IsChecked = state.Toggle,
            Content = item.Hint,
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

        return box;
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
            FontSize = 12,
            Opacity = 0.75,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        stack.Children.Add(text);
        return stack;
    }
}
