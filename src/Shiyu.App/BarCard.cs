using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// One card's view state. A class rather than a record because selection is
/// mutable and the card's own highlight follows it.
/// </summary>
internal sealed class BarCard : INotifyPropertyChanged
{
    public long Id { get; init; }

    public EntryKind Kind { get; init; }

    /// <summary>The full text, kept for copying; the card shows the clamped preview.</summary>
    public required string Text { get; init; }

    public string Preview { get; init; } = string.Empty;

    /// <summary>How to drag out, shown under the preview in the tooltip.</summary>
    public string DragHint { get; init; } = string.Empty;

    /// <summary>The card's tooltip: its content, and what holding it does.</summary>
    public string DragToolTip => string.IsNullOrEmpty(Preview)
        ? DragHint
        : Preview + Environment.NewLine + Environment.NewLine + DragHint;

    public string KindText { get; init; } = string.Empty;

    public string WhenText { get; init; } = string.Empty;

    /// <summary>The absolute stamp (and usage count) behind the relative one.</summary>
    public string WhenToolTip { get; init; } = string.Empty;

    /// <summary>
    /// The number key that pastes this row, when it is one of the first ten
    /// displayed rows; null otherwise. Assigned from display position.
    /// </summary>
    public string? RowKeyText { get; set; }

    public ImageSource? Icon { get; init; }

    public ImageSource? Thumbnail { get; init; }

    public string? OriginalPath { get; init; }

    public bool HasOriginal { get; init; }

    public EntrySubtype Subtype { get; init; }

    /// <summary>The copy's HTML form, kept so pasting back into a rich destination keeps its formatting.</summary>
    public string? Html { get; init; }

    public string? Rtf { get; init; }

    /// <summary>The file rows a file card shows, already clamped to the density knob.</summary>
    public IReadOnlyList<FileRow> FileRows { get; init; } = [];

    /// <summary>A file copy made entirely of images previews its first file.</summary>
    public ImageSource? FilePreviewSource { get; init; }

    /// <summary>True when this entry is Shiyu's own kept translation of another.</summary>
    public bool IsTranslation { get; init; }

    public Visibility TranslationVisibility => IsTranslation
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility FilePreviewVisibility => FilePreviewSource is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    /// <summary>The full capped path list of a file entry, for copying and pasting back.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    private bool _favorite;

    /// <summary>Mutates in place: favouriting must not disturb the list around it.</summary>
    public bool Favorite
    {
        get => _favorite;
        set
        {
            _favorite = value;
            Changed(nameof(Favorite));
            // The watermark binds the visibility, not the flag — without this
            // the star never appears, and un-favouriting looks impossible.
            Changed(nameof(FavoriteVisibility));
        }
    }

    public Visibility FavoriteVisibility => Favorite ? Visibility.Visible : Visibility.Collapsed;

    private string? _groupBadge;

    /// <summary>
    /// The name of the pile this entry was filed into. Mutates in place, like
    /// the favourite flag: filing is organisation, not reordering.
    /// </summary>
    public string? GroupBadge
    {
        get => _groupBadge;
        set
        {
            _groupBadge = value;
            Changed(nameof(GroupBadge));
            Changed(nameof(GroupBadgeVisibility));
        }
    }

    public Visibility GroupBadgeVisibility => string.IsNullOrEmpty(GroupBadge)
        ? Visibility.Collapsed
        : Visibility.Visible;

    private string? _note;

    /// <summary>Mutates in place: the note becomes the entry's public face the moment it is saved.</summary>
    public string? Note
    {
        get => _note;
        set
        {
            _note = value;
            Changed(nameof(Note));
        }
    }

    private string _face = string.Empty;

    /// <summary>What the body shows: the note by default, the original on hover.</summary>
    public string Face
    {
        get => _face;        set
        {
            _face = value;
            Changed(nameof(Face));
        }
    }

    /// <summary>True when every path of a file entry is gone — the card shows it struck through and faded.</summary>
    public bool AllPathsDead { get; init; }

    public int FileCount { get; init; }

    public Visibility FilesVisibility =>
        Kind == EntryKind.Files ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The quiet count line under the file rows.</summary>
    public string FileTail => FileCount switch
    {
        0 => string.Empty,
        1 => "1 个项目",
        _ => $"共 {FileCount} 项",
    };

    /// <summary>Links and emails open in the system's default program.</summary>
    public bool IsOpenable => Subtype is EntrySubtype.Link or EntrySubtype.Email;

    /// <summary>The parsed colour of a colour entry, as a frozen brush ready to paint.</summary>
    public Brush? SwatchBrush { get; init; }

    public Visibility SwatchVisibility =>
        SwatchBrush is null ? Visibility.Collapsed : Visibility.Visible;

    public int TextLines { get; init; }

    /// <summary>
    /// The text clamp as a height of whole Body lines — visually identical to
    /// MaxLines, usable from XAML on this build (see the template comment).
    /// </summary>
    public double TextMaxHeight
        => TextLines * DesignTokens.LineHeightFor(DesignTokens.FontBody);

    public int ImageHeight { get; init; }

    /// <summary>The original image's pixel size, for the preview panel's pre-computed shape.</summary>
    public int PixelWidth { get; init; }

    public int PixelHeight { get; init; }

    public bool IsPinned { get; init; }

    public Visibility IconVisibility => Icon is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility FallbackIconVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ImageVisibility =>
        Kind == EntryKind.Image ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PinnedVisibility => IsPinned ? Visibility.Visible : Visibility.Collapsed;

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            Changed(nameof(IsSelected));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One row of a file card: a name, its type icon, and whether the path still exists.</summary>
internal sealed record FileRow(string Name, string FullPath, ImageSource? Icon, bool Dead)
{
    public Visibility DeadVisibility => Dead ? Visibility.Visible : Visibility.Collapsed;
}
