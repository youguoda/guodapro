namespace Shiyu.Core;

/// <summary>A copied image, ready to be decoded and encoded away from the user interface thread.</summary>
public interface IClipboardImage
{
    /// <summary>
    /// Decodes the captured bytes and encodes the stored forms. Awaited off
    /// the thread that draws, because decoding and encoding a screenshot take
    /// long enough to be felt as a stutter if they are not — the interface
    /// thread only ever takes the raw bytes.
    /// </summary>
    Task<RenderedImage> RenderAsync(CancellationToken cancellation = default);
}

/// <param name="FullPng">The original, destined for disk and for retention.</param>
/// <param name="ThumbnailPng">Small, kept in the database, kept forever.</param>
/// <param name="Fingerprint">
/// A content hash of the decoded pixels, for recognising the same copy
/// arriving twice — applications publish one image in several clipboard
/// formats in turn, and each publication raises its own notification. Zero
/// means "no fingerprint"; such images are never collapsed, so an
/// implementation that cannot hash simply omits it.
/// </param>
public sealed record RenderedImage(
    byte[] FullPng,
    byte[] ThumbnailPng,
    int Width,
    int Height,
    long Fingerprint = 0);

/// <summary>
/// What the operating system reported when the clipboard changed.
/// </summary>
/// <param name="Text">The copied text, or a stand-in label for an image.</param>
/// <param name="SourceApp">The application that was in the foreground, if known.</param>
/// <param name="ExcludedByMarker">
/// Whether the copying application asked clipboard tools to leave this content
/// alone. Detecting the request belongs to the platform; deciding what to do
/// about it belongs to <see cref="ExclusionPolicy"/>.
/// </param>
public sealed record ClipboardSnapshot(string Text, string? SourceApp, bool ExcludedByMarker)
{
    /// <summary>Set when an image was copied rather than text.</summary>
    public IClipboardImage? Image { get; init; }

    /// <summary>
    /// The foreground application's executable, taken at copy time — the only
    /// moment the icon of an application that might later be uninstalled is
    /// guaranteed to be extractable. Belongs to the snapshot for the same
    /// reason <see cref="SourceApp"/> does.
    /// </summary>
    public string? SourceExePath { get; init; }

    /// <summary>
    /// The copy's HTML form, when the source published one. Kept so a paste
    /// back into a rich-text destination keeps its formatting; the plain
    /// <see cref="Text"/> remains what the list shows and searches.
    /// </summary>
    public string? Html { get; init; }

    /// <summary>The copy's RTF form, when the source published one.</summary>
    public string? Rtf { get; init; }

    /// <summary>
    /// The paths of a file copy, when the clipboard carried CF_HDROP instead of
    /// text or a bitmap. One copy of any number of files is one snapshot.
    /// </summary>
    public IReadOnlyList<string>? Files { get; init; }
}
