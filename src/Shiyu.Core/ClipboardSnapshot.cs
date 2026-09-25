namespace Shiyu.Core;

/// <summary>A copied image, ready to be encoded away from the user interface thread.</summary>
public interface IClipboardImage
{
    /// <summary>
    /// Encodes the image. Awaited off the thread that draws, because encoding a
    /// screenshot takes long enough to be felt as a stutter if it is not.
    /// </summary>
    Task<RenderedImage> RenderAsync(CancellationToken cancellation = default);
}

/// <param name="FullPng">The original, destined for disk and for retention.</param>
/// <param name="ThumbnailPng">Small, kept in the database, kept forever.</param>
public sealed record RenderedImage(byte[] FullPng, byte[] ThumbnailPng, int Width, int Height);

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
}
