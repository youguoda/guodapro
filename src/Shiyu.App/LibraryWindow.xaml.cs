using System.Collections.Specialized;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>
/// The library: the place for the weekly sort-through, as opposed to the quick
/// bar's dozens-of-times-a-day paste. It may be large and may linger.
/// </summary>
public partial class LibraryWindow : Window
{
    /// <summary>
    /// Long enough that typing a word is one search rather than six, short
    /// enough that the list feels like it is keeping up.
    /// </summary>
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(180);

    private readonly EntryStore _store;
    private readonly WindowsClipboardWriter _clipboard;
    private readonly ImageArchive _images;
    private readonly HistoryBrowser _browser;
    private readonly ObservableCollection<EntryItem> _items = [];
    private readonly DispatcherTimer _searchDebounce;

    /// <summary>The "no tag filter" choice, shown as the first item.</summary>
    private const string AnyTag = "全部";

    private bool _refillingTags;

    public LibraryWindow(EntryStore store, WindowsClipboardWriter clipboard, ImageArchive images)
    {
        InitializeComponent();

        _store = store;
        _clipboard = clipboard;
        _images = images;
        _browser = new HistoryBrowser(store);

        _searchDebounce = new DispatcherTimer { Interval = SearchDelay };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            ApplyFilter();
        };

        EntryList.ItemsSource = _items;
        RefreshTagChoices();
        Reload();
    }

    /// <summary>Reads the first page again, e.g. after the history changed underneath.</summary>
    public void Reload()
    {
        _browser.Reset();
        Rebuild();
    }

    private void Rebuild()
    {
        _items.Clear();
        Append(_browser.Loaded);
        UpdateChrome();
    }

    private void Append(IEnumerable<Entry> entries)
    {
        foreach (var entry in entries)
        {
            _items.Add(EntryItem.From(entry));
        }
    }

    private void UpdateChrome()
    {
        CountLabel.Text = $"共 {_store.Count()} 条";
        EmptyLabel.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLabel.Text = _browser.Filter.IsEmpty ? "还没有记录" : "没有符合筛选条件的记录";
    }

    /// <summary>
    /// Rebuilds the filter from every control at once. Assembled in one place
    /// because the parts combine — narrowing by type and by date together is
    /// the whole point — and reading them piecemeal invites them to drift.
    /// </summary>
    private void ApplyFilter()
    {
        _browser.Filter = new HistoryFilter
        {
            Query = SearchBox.Text,
            Tag = TagFilter.SelectedItem as string is { } tag && tag != AnyTag ? tag : null,
            Kind = KindFilter.SelectedIndex switch
            {
                1 => EntryKind.Text,
                2 => EntryKind.Image,
                _ => null,
            },
            From = FilterFrom.SelectedDate is { } from
                ? new DateTimeOffset(from.Date, DateTimeOffset.Now.Offset)
                : null,

            // Whole days: a user picking today means all of today, not the
            // instant midnight began.
            To = FilterTo.SelectedDate is { } to
                ? new DateTimeOffset(to.Date.AddDays(1).AddTicks(-1), DateTimeOffset.Now.Offset)
                : null,
        };

        Rebuild();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        // Refilling the tag list raises a selection change of its own; acting
        // on it would reset the very filter the user just set.
        if (IsLoaded && !_refillingTags)
        {
            ApplyFilter();
        }
    }

    /// <summary>Refills the tag list, keeping the current choice if it survives.</summary>
    private void RefreshTagChoices()
    {
        var chosen = TagFilter.SelectedItem as string;
        _refillingTags = true;

        TagFilter.Items.Clear();
        TagFilter.Items.Add(AnyTag);
        foreach (var tag in _store.AllTags())
        {
            TagFilter.Items.Add(tag);
        }

        TagFilter.SelectedItem = chosen is not null && TagFilter.Items.Contains(chosen)
            ? chosen
            : AnyTag;

        _refillingTags = false;
    }

    /// <summary>
    /// Keeps the pin button honest about what it will do. A button that always
    /// reads "置顶" while pointing at a pinned entry invites the wrong click.
    /// </summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        => PinButton.Content = EntryList.SelectedItem is EntryItem { IsPinned: true }
            ? "取消置顶"
            : "置顶";

    private void OnTogglePin(object sender, RoutedEventArgs e)
    {
        if (EntryList.SelectedItem is not EntryItem item)
        {
            return;
        }

        _store.SetPinned(item.Id, !item.IsPinned);

        // Reloaded rather than patched in place: pinning changes where the
        // entry belongs in the list, not just how it looks.
        Reload();
        Status(item.IsPinned ? "已取消置顶" : "已置顶");
    }

    private void OnTagBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OnAddTag(sender, e);
        }
    }

    private void OnAddTag(object sender, RoutedEventArgs e)
    {
        if (EntryList.SelectedItem is not EntryItem item || TagBox.Text.Trim().Length == 0)
        {
            return;
        }

        _store.AddTag(item.Id, TagBox.Text);
        var added = TagBox.Text.Trim();
        TagBox.Text = string.Empty;

        RefreshTagChoices();
        Reload();
        Status($"已加标签「{added}」");
    }

    private void OnRemoveTag(object sender, RoutedEventArgs e)
    {
        if (EntryList.SelectedItem is not EntryItem item || TagBox.Text.Trim().Length == 0)
        {
            return;
        }

        _store.RemoveTag(item.Id, TagBox.Text);
        var removed = TagBox.Text.Trim();
        TagBox.Text = string.Empty;

        RefreshTagChoices();
        Reload();
        Status($"已去掉标签「{removed}」");
    }

    private void OnClearFilters(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        KindFilter.SelectedIndex = 0;
        TagFilter.SelectedItem = AnyTag;
        FilterFrom.SelectedDate = null;
        FilterTo.SelectedDate = null;
        ApplyFilter();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Restarted on every keystroke, so only the pause at the end searches.
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void OnListScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange <= 0 || !_browser.HasMore)
        {
            return;
        }

        // Fetch the next page slightly before the user reaches the bottom, so
        // scrolling does not stutter at the seam.
        var remaining = e.ExtentHeight - e.VerticalOffset - e.ViewportHeight;
        if (remaining > e.ViewportHeight)
        {
            return;
        }

        var before = _browser.Loaded.Count;
        if (_browser.LoadMore() > 0)
        {
            Append(_browser.Loaded.Skip(before));
        }
    }

    /// <summary>
    /// Starts a file drag for an image entry, so it can be dropped straight
    /// into Explorer or another application.
    ///
    /// The original is already a real file on disk, so this is an ordinary file
    /// drag — no virtual-file plumbing needed.
    /// </summary>
    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || EntryList.SelectedItem is not EntryItem item)
        {
            return;
        }

        if (!item.CanDrag)
        {
            return;
        }

        var files = new StringCollection { item.OriginalPath! };
        var payload = new DataObject();
        payload.SetFileDropList(files);

        DragDrop.DoDragDrop(EntryList, payload, DragDropEffects.Copy);
    }

    private void OnCopySelected(object sender, RoutedEventArgs e)
    {
        if (EntryList.SelectedItem is not EntryItem item)
        {
            return;
        }

        if (item.Thumbnail is not null)
        {
            // The clipboard writer handles text; putting a bitmap back would be
            // a separate piece of interop this feature does not need. Dragging
            // the file out covers what the user actually wants to do with it.
            Status("图片条目请直接拖出到文件夹另存");
            return;
        }

        if (!_clipboard.SetText(item.Text))
        {
            Status("复制失败：剪贴板被其他程序占用，稍后再试");
            return;
        }

        // Shiyu suppresses its own clipboard writes, so re-copying would
        // otherwise leave the entry where it was. Moving it to the top is what
        // the user just expressed a preference for.
        _store.Touch(item.Id, DateTimeOffset.UtcNow);
        Status("已复制");
    }

    private void OnDeleteSelected(object sender, RoutedEventArgs e)
    {
        if (EntryList.SelectedItem is not EntryItem item)
        {
            return;
        }

        var index = EntryList.SelectedIndex;

        // The original goes with the entry. Leaving it behind would accumulate
        // files nothing references and nothing will ever clean up.
        if (item.OriginalPath is { Length: > 0 } original)
        {
            _images.Delete(original);
        }

        _store.Delete(item.Id);
        _browser.Forget(item.Id);
        _items.Remove(item);

        // Keep the user where they were rather than sending them to the top.
        EntryList.SelectedIndex = System.Math.Min(index, _items.Count - 1);
        UpdateChrome();
        Status("已删除 1 条");
    }

    private void OnClearAll(object sender, RoutedEventArgs e)
    {
        var total = _store.Count();
        if (total == 0)
        {
            return;
        }

        // Irreversible and one click away from the ordinary buttons, so it asks
        // — and says how much is at stake rather than a generic "are you sure".
        var answer = MessageBox.Show(
            this,
            $"将永久删除全部 {total} 条历史记录，无法撤销。确定吗？",
            "清空全部历史",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        DeleteOriginalsOf(_store.ImagesWithOriginals());
        var removed = _store.DeleteAll();
        Reload();
        Status($"已清空 {removed} 条");
    }

    private void OnDeleteRange(object sender, RoutedEventArgs e)
    {
        if (RangeFrom.SelectedDate is not { } from || RangeTo.SelectedDate is not { } to)
        {
            Status("请先选择起止日期");
            return;
        }

        if (to < from)
        {
            (from, to) = (to, from);
        }

        // Whole days, inclusive: a user picking the same date twice means that
        // day, not the single instant at midnight.
        var start = new DateTimeOffset(from.Date, DateTimeOffset.Now.Offset);
        var end = new DateTimeOffset(to.Date.AddDays(1).AddTicks(-1), DateTimeOffset.Now.Offset);

        var answer = MessageBox.Show(
            this,
            $"将永久删除 {from:yyyy-MM-dd} 至 {to:yyyy-MM-dd} 之间的全部记录，无法撤销。确定吗？",
            "按时间段删除",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        DeleteOriginalsOf(_store.ImagesCreatedBetween(start, end));
        var removed = _store.DeleteCreatedBetween(start, end);
        Reload();
        Status($"已删除 {removed} 条");
    }

    /// <summary>Removes the files behind image entries that are about to go.</summary>
    private void DeleteOriginalsOf(IEnumerable<Entry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.OriginalPath is { Length: > 0 } path)
            {
                _images.Delete(path);
            }
        }
    }

    private void Status(string message) => StatusLabel.Text = message;

    private sealed record EntryItem(
        long Id,
        string Text,
        string Preview,
        string Meta,
        ImageSource? Thumbnail,
        string? OriginalPath,
        bool IsPinned)
    {
        public Visibility PinVisibility => IsPinned ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ThumbnailVisibility =>
            Thumbnail is null ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>True while the full-size image is still on disk.</summary>
        public bool CanDrag => OriginalPath is { Length: > 0 } path && File.Exists(path);

        public static EntryItem From(Entry entry)
        {
            var collapsed = string.Join(' ', entry.Text.Split(
                ['\r', '\n', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            var preview = collapsed.Length > 300 ? collapsed[..300] + "…" : collapsed;
            var source = string.IsNullOrEmpty(entry.SourceApp) ? "未知来源" : entry.SourceApp;

            var tags = entry.Tags.Count == 0 ? string.Empty : "  ·  " + string.Join(" ", entry.Tags.Select(t => "#" + t));

            var tail = entry.Kind == EntryKind.Image
                ? entry.HasOriginal ? "可拖出另存" : "原图已过期清理"
                : $"{entry.Text.Length} 字";

            return new EntryItem(
                entry.Id,
                entry.Text,
                preview,
                $"{entry.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}  ·  {source}  ·  {tail}{tags}",
                Decode(entry.ThumbnailPng),
                entry.OriginalPath,
                entry.IsPinned);
        }

        private static ImageSource? Decode(byte[]? png)
        {
            if (png is null or { Length: 0 })
            {
                return null;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(png);
                bitmap.EndInit();

                // Frozen so the list can recycle rows across threads freely.
                bitmap.Freeze();
                return bitmap;
            }
            catch (NotSupportedException)
            {
                // A thumbnail that will not decode is not worth a broken window.
                return null;
            }
        }
    }
}
