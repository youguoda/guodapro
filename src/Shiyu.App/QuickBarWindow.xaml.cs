using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>
/// The quick bar: summoned, chosen from with the keyboard, pasted, gone. It
/// serves the dozens-of-times-a-day paste, so it has to feel like it is barely
/// there.
///
/// Unlike the badge and the panel, this window <em>does</em> take focus — it
/// cannot be driven by the keyboard otherwise. The window the user was typing
/// in is noted before it appears and restored before anything is pasted.
/// </summary>
public partial class QuickBarWindow : Window
{
    private const int PageSize = 60;

    private readonly EntryStore _store;
    private readonly SelectionCapture _capture;
    private readonly ObservableCollection<QuickItem> _items = [];

    private ForegroundWindow _returnTo;

    public QuickBarWindow(EntryStore store, SelectionCapture capture)
    {
        InitializeComponent();

        _store = store;
        _capture = capture;
        Items.ItemsSource = _items;

        Backdrop.Attach(this, () => BackdropKind.Acrylic);
    }

    /// <summary>Shows the bar beside the cursor, ready for the keyboard.</summary>
    public void Summon()
    {
        // Noted before this window steals the foreground, which it is about to.
        _returnTo = ForegroundWindow.Current();

        FilterBox.Text = string.Empty;
        Reload();

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        MoveBesideCursor();

        Activate();
        FilterBox.Focus();
    }

    private void MoveBesideCursor()
    {
        var cursor = ScreenGeometry.CursorPosition();
        var workArea = ScreenGeometry.WorkAreaAt(cursor);

        var source = PresentationSource.FromVisual(this);
        var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        var placed = BadgePlacement.Place(
            cursor,
            (int)Math.Ceiling(ActualWidth * scaleX),
            (int)Math.Ceiling(ActualHeight * scaleY),
            workArea);

        // Moved with SetWindowPos for the same reason as the badge: physical
        // pixels sidestep the scaled coordinate system entirely.
        TransientWindow.MoveTo(new WindowInteropHelper(this).Handle, placed);
    }

    private void Reload()
    {
        var query = FilterBox.Text;
        var entries = string.IsNullOrWhiteSpace(query)
            ? _store.Page(PageSize, offset: 0)
            : _store.Search(query, PageSize);

        _items.Clear();
        foreach (var entry in entries)
        {
            _items.Add(QuickItem.From(entry));
        }

        EmptyLabel.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Always leaves something selected, so Enter has an obvious meaning the
        // moment the bar appears.
        if (_items.Count > 0)
        {
            Items.SelectedIndex = 0;
        }
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => Reload();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Dismiss();
                break;

            case Key.Enter:
                e.Handled = true;
                PasteSelected();
                break;

            // Handled here rather than left to the list, because focus stays in
            // the filter box: the user types to narrow and arrows to choose
            // without ever moving between controls.
            case Key.Down:
                e.Handled = true;
                Move(1);
                break;

            case Key.Up:
                e.Handled = true;
                Move(-1);
                break;
        }
    }

    private void Move(int delta)
    {
        if (_items.Count == 0)
        {
            return;
        }

        var next = Math.Clamp(Items.SelectedIndex + delta, 0, _items.Count - 1);
        Items.SelectedIndex = next;
        Items.ScrollIntoView(_items[next]);
    }

    private void OnItemActivated(object sender, MouseButtonEventArgs e) => PasteSelected();

    private void PasteSelected()
    {
        if (Items.SelectedItem is not QuickItem item)
        {
            return;
        }

        // The entry came back into the world; that is what a use is.
        if (item.Id is { } id)
        {
            _store.BumpUse(id);
        }

        // Hidden first: the paste has to land in the user's application, and
        // this window is in the way of the foreground until it goes.
        Hide();
        _returnTo.Restore();

        _capture.Paste(item.Text);
    }

    /// <summary>Clicking elsewhere means the user moved on; do not paste.</summary>
    private void OnDeactivated(object sender, EventArgs e) => Dismiss();

    private void Dismiss()
    {
        if (IsVisible)
        {
            Hide();
            _returnTo.Restore();
        }
    }

    /// <summary>Lets the application close it for real on shutdown.</summary>
    public void CloseForGood() => Close();

    private sealed record QuickItem(long? Id, string Text, string Preview, string Meta)
    {
        public static QuickItem From(Entry entry)
        {
            var collapsed = string.Join(' ', entry.Text.Split(
                ['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            var preview = collapsed.Length > 160 ? collapsed[..160] + "…" : collapsed;
            var source = string.IsNullOrEmpty(entry.SourceApp) ? "未知来源" : entry.SourceApp;

            return new QuickItem(
                entry.Id,
                entry.Text,
                preview,
                $"{entry.CreatedAt.ToLocalTime():MM-dd HH:mm} · {source}");
        }
    }
}
