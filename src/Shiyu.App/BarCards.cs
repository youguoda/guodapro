using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shiyu.Core;

namespace Shiyu.App;

internal partial class BarWindow
{
    // --- cards ----------------------------------------------------------------

    private BarCard CardFor(Entry entry, IReadOnlyDictionary<long, EntryBlobs>? blobs = null)
    {
        var collapsed = string.Join(' ', entry.Text.Split(
            ['\r', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var shown = Math.Max(1, _settings.BarFileCount);
        var fileRows = entry.Kind == EntryKind.Files
            ? entry.Files.Take(shown)
                .Select(path => new FileRow(
                    Path.GetFileName(path) is { Length: > 0 } name ? name : path,
                    path,
                    _fileIcons.For(path),
                    !File.Exists(path)))
                .ToList()
            : [];

        // List rows are narrow by design (O-22); the thumbnail a card shows
        // comes from the page's one BlobsOf batch, and the formatted forms are
        // fetched at the moment an action needs them.
        var payload = blobs?.GetValueOrDefault(entry.Id);

        var card = new BarCard
        {
            Id = entry.Id,
            Kind = entry.Kind,
            Text = entry.Text,
            Preview = collapsed.Length > 500 ? collapsed[..500] + "…" : collapsed,
            DragHint = "🖱 按住左键拖出：文本入编辑器（带格式）、图片入聊天窗、文件入资源管理器",
            KindText = entry.Kind switch
            {
                EntryKind.Image => "图片",
                EntryKind.Files => "文件",
                _ => entry.TranslatedFrom is null ? "文本" : "译文 · 译自原文",
            },
            KindGlyph = entry.Kind switch
            {
                EntryKind.Image => "\uE8B9",
                EntryKind.Files => "\uE8B7",
                _ => "\uE8D2",
            },
            UseCount = entry.UseCount,

            // Relative for the scan, absolute for the hover; the usage count
            // lives on the type badge's tooltip (票 39) — a reward read on
            // demand, not an identity worn on the face.
            WhenText = RelativeTime.For(entry.CreatedAt, DateTimeOffset.Now),
            WhenToolTip = $"{entry.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}",
            Icon = _icons.For(entry.SourceApp),
            Thumbnail = entry.Kind == EntryKind.Image
                ? AppIconCache.Decode(payload?.ThumbnailPng, 320)
                : null,
            OriginalPath = entry.OriginalPath,
            HasOriginal = entry.HasOriginal,
            Subtype = entry.Subtype,
            Files = entry.Files,
            Favorite = entry.Favorite,
            Note = entry.Note,
            GroupBadge = entry.GroupId is { } filedInto && _groupsById.TryGetValue(filedInto, out var pile)
                ? pile.Name
                : null,
            IsTranslation = entry.TranslatedFrom is not null,
            FileRows = fileRows,
            FilePreviewSource = PreviewFileImage(entry),
            AllPathsDead = entry.Kind == EntryKind.Files && entry.Files.All(path => !File.Exists(path)),
            FileCount = entry.Files.Count,
            SwatchBrush = entry.Subtype == EntrySubtype.Color
                && SubtypeColor.TryParse(entry.Text, out var colour)
                ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(colour.A, colour.R, colour.G, colour.B))
                    .FrozenBrush()
                : null,
            TextLines = Math.Max(1, _settings.BarTextLines),
            ImageHeight = Math.Max(24, _settings.BarImageHeight),
            PixelWidth = entry.ImageWidth,
            PixelHeight = entry.ImageHeight,
            IsPinned = entry.IsPinned,
            IsProtected = _settings.ProtectEntries
                && ((_settings.ProtectFavorites && entry.Favorite)
                    || (_settings.ProtectPinned && entry.IsPinned)),
        };

        // The note is the public face; the original waits behind a hover.
        card.Face = entry.Note is { Length: > 0 } ? entry.Note : card.Preview;
        return card;
    }

    /// <summary>
    /// A file copy made entirely of images previews as pictures: names alone
    /// answer "which file", not "what was in it". A missing or unreadable file
    /// falls back to the rows — a struck-through name says more than nothing.
    /// </summary>
    private static ImageSource? PreviewFileImage(Entry entry)
    {
        if (entry.Kind != EntryKind.Files
            || FileEntries.PreviewImagePath(entry.Files) is not { } path
            || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 320;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            // A file with an image extension that is not a readable bitmap.
            return null;
        }
    }
    private void Rebuild()
    {
        _pinned.Clear();
        _cards.Clear();
        _selected = null;
        Append(_browser.Loaded);
        PinnedHost.Visibility = _pinned.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCount();
        UpdateEmptyState();
        EnsureActiveItem();
    }

    /// <summary>
    /// The empty panel says which conditions emptied the list — a filter that
    /// ate everything is recoverable, and the words are the recovery map.
    /// </summary>
    private void UpdateEmptyState()
    {
        var empty = _pinned.Count == 0 && _cards.Count == 0;
        EmptyHost.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        if (!empty)
        {
            return;
        }

        var groupName = _selectedGroup is { } picked && _groupsById.TryGetValue(picked, out var group)
            ? group.Name
            : null;

        var copy = EmptyStates.For(
            new HistoryFilter
            {
                Query = SearchBox.Text,
                Favorite = FavoriteOnly.IsChecked == true ? true : null,
                Kind = _kindIndex switch
                {
                    1 => EntryKind.Text,
                    2 => EntryKind.Image,
                    3 => EntryKind.Files,
                    _ => null,
                },
                Group = _selectedGroup,
            },
            groupName,
            _store.Count());

        EmptyHeadline.Text = copy.Headline;
        EmptyHint.Text = copy.Hint;
        EmptyClear.Visibility = copy.OfferClear ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Append(IEnumerable<Entry> entries)
    {
        var batch = entries.ToList();

        // The one payload read per page: only image cards want a thumbnail,
        // so the batch asks for those ids alone rather than dragging payloads
        // through the list query (O-22).
        var blobs = _store.BlobsOf(
            [.. batch.Where(entry => entry.Kind == EntryKind.Image).Select(entry => entry.Id)]);

        // The store orders pinned first, so the partition is one pass: once
        // the first unpinned entry arrives, everything after it is too.
        var pastPinned = _cards.Count > 0;

        foreach (var entry in batch)
        {
            if (!entry.IsPinned)
            {
                pastPinned = true;
            }

            var card = CardFor(entry, blobs);
            card.RowKeyText = BarKeys.RowKey(_pinned.Count + _cards.Count + 1);
            (pastPinned ? _cards : _pinned).Add(card);
        }
    }

    /// <summary>
    /// The footer's three voices: the count (total, or "3 / 161" when a filter
    /// is on — a paged list cannot count itself), the action feedback, and the
    /// Esc hint, which follows the escape stack instead of asserting one
    /// behaviour while another is in force.
    /// </summary>
    private void UpdateFooter()
    {
        if (_feedbackHostOpen || _hintShowing)
        {
            return;
        }

        var total = _store.Count();
        var filtered = _store.CountMatching(_browser.Filter);
        var escHint = _browser.Filter.IsEmpty ? "Esc 隐藏" : "Esc 清除筛选";

        CountLabel.Text = filtered == total
            ? $"共 {total} 条 · {escHint}"
            : $"{filtered} / {total} 条 · {escHint}";
    }

    private void UpdateCount() => UpdateFooter();

    /// <summary>Whether a feedback row is on show (and owns the footer).</summary>
    private bool _feedbackHostOpen;

    private System.Windows.Threading.DispatcherTimer? _feedbackTimer;

    private IReadOnlyList<(Entry Entry, string? Group)>? _undoPending;

    /// <summary>
    /// One feedback row at a time; an undo keeps it on show for the full five
    /// seconds the user was promised, a plain confirmation leaves quickly.
    /// </summary>
    private void ShowFeedback(string text, IReadOnlyList<(Entry Entry, string? Group)>? undo = null)
    {
        _feedbackTimer?.Stop();
        _feedbackHostOpen = true;
        _undoPending = undo;

        FeedbackLabel.Text = text;
        UndoButton.Visibility = undo is null ? Visibility.Collapsed : Visibility.Visible;
        FeedbackHost.Visibility = Visibility.Visible;
        CountLabel.Visibility = Visibility.Collapsed;

        _feedbackTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = undo is null ? TimeSpan.FromSeconds(1.6) : TimeSpan.FromSeconds(5),
        };
        _feedbackTimer.Tick += (_, _) => ClearFeedback();
        _feedbackTimer.Start();
    }

    private void ClearFeedback()
    {
        _feedbackTimer?.Stop();
        _feedbackTimer = null;
        _feedbackHostOpen = false;
        _undoPending = null;
        FeedbackHost.Visibility = Visibility.Collapsed;
        CountLabel.Visibility = Visibility.Visible;
        UpdateFooter();
    }

    private void OnUndoDelete(object sender, RoutedEventArgs e) => RestoreUndo();

    private void RestoreUndo()
    {
        if (_undoPending is not { } items)
        {
            return;
        }

        foreach (var (entry, group) in items)
        {
            // Re-filing by group name recreates the group if it went away
            // mid-window, which is the same promise delete made about entries.
            SelfWrite(() => _store.ImportEntry(entry, group));
        }

        ClearFeedback();
        ApplyFilter();
        ShowFeedback($"已恢复 {items.Count} 条");
    }

    // --- first-use hints ---------------------------------------------------------

    /// <summary>Whether the footer is currently teaching instead of counting.</summary>
    private bool _hintShowing;

    private void ShowFirstUseHint()
    {
        _hintShowing = true;
        FeedbackLabel.Text = FirstUseHints.Text();
        UndoButton.Visibility = Visibility.Collapsed;
        FeedbackHost.Visibility = Visibility.Visible;
        CountLabel.Visibility = Visibility.Collapsed;
    }

    private void DismissFirstUseHint()
    {
        if (!_hintShowing)
        {
            return;
        }

        _hintShowing = false;
        FeedbackHost.Visibility = Visibility.Collapsed;
        CountLabel.Visibility = Visibility.Visible;
        UpdateFooter();
    }

    /// <summary>
    /// The store changed underneath — a copy from anywhere, a retention sweep,
    /// an import, an edit in the library window. Reload while visible; while
    /// hidden there is nothing to refresh and Summon reads fresh anyway, which
    /// is exactly the boundary the old two-second probe kept.
    /// </summary>
    private void OnStoreChanged()
    {
        // This window's own writes already produced exactly the visual change
        // they meant to — favourite and note update their card in place, pin
        // reloads itself — so a reload here would only reset the scroll under
        // the user (O-37). The depth is read on the writer's thread; a benign
        // race with a background write costs one extra reload, never a miss.
        if (_selfWrites > 0 || !IsVisible)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            ReloadData();
        }
        else
        {
            // External writes arrive on the pipeline's thread; the cards
            // belong to the dispatcher's.
            Dispatcher.BeginInvoke(ReloadData);
        }
    }

    /// <summary>How many of this window's own writes are in flight.</summary>
    private int _selfWrites;

    /// <summary>
    /// Runs one of this window's own writes, whose Changed event it intends
    /// to ignore because it handles the visual itself.
    /// </summary>
    private void SelfWrite(Action write)
    {
        _selfWrites++;
        try
        {
            write();
        }
        finally
        {
            _selfWrites--;
        }
    }

    private void OnCardsScrolled(object sender, ScrollChangedEventArgs e)
    {
        // Scrolling says the user has moved on: a hover preview goes, a
        // held-space preview answers to its key alone.
        RunPreviewCommand(_previewPolicy.Scrolled());

        if (!_browser.HasMore
            || e.VerticalOffset + e.ViewportHeight < e.ExtentHeight - 400)
        {
            return;
        }

        var added = _browser.LoadMore();
        if (added > 0)
        {
            Append(_browser.Loaded.Skip(_browser.Loaded.Count - added));
        }
    }

    private void ReloadData()
    {
        _browser.Reset();
        RefreshTagChoices();
        Rebuild();
    }

    private void RemoveCard(BarCard card)
    {
        // A preview of a card that no longer exists is a panel describing a
        // ghost; it comes down before the row does.
        if (_preview is { IsVisible: true, CardId: var shown } && shown == card.Id)
        {
            RunPreviewCommand(_previewPolicy.Escape());
        }

        var inRest = _cards.IndexOf(card);
        if (inRest >= 0)
        {
            _cards.RemoveAt(inRest);
        }
        else
        {
            _pinned.Remove(card);
        }

        if (_pinned.Count == 0)
        {
            PinnedHost.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// The success feedback: the pressed button becomes a tick for a second,
    /// and — only if the user asked — a sound joins it.
    /// </summary>
    private void Confirm(Button? button, bool succeeded)
    {
        if (button is null)
        {
            return;
        }

        if (!succeeded)
        {
            button.Content = "✗";
            return;
        }

        if (_settings.ActionSound)
        {
            System.Media.SystemSounds.Asterisk.Play();
        }

        var glyph = button.Content;
        button.Content = "✓";

        var restore = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        restore.Tick += (_, _) =>
        {
            restore.Stop();
            button.Content = glyph;
        };
        restore.Start();
    }
}
