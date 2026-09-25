using Shiyu.Core;

namespace Shiyu.Core.Tests.Fakes;

/// <summary>
/// Stands in for the operating system's clipboard notifications so tests can
/// drive the pipeline directly, including the awkward cases real applications
/// produce.
/// </summary>
public sealed class FakeClipboardMonitor : IClipboardMonitor
{
    public event Action<ClipboardSnapshot>? Changed;

    public void Emit(string text, string? sourceApp = null, string? sourceExePath = null, bool excluded = false,
        string? html = null, string? rtf = null)
        => Changed?.Invoke(new ClipboardSnapshot(text, sourceApp, excluded)
        {
            SourceExePath = sourceExePath,
            Html = html,
            Rtf = rtf,
        });

    /// <summary>Mimics an application that asked clipboard tools to leave it alone.</summary>
    public void EmitExcluded(string text, string? sourceApp = null)
        => Changed?.Invoke(new ClipboardSnapshot(text, sourceApp, ExcludedByMarker: true));

    /// <summary>Mimics a copied image.</summary>
    public void EmitImage(IClipboardImage image, string? sourceApp = null, bool excluded = false)
        => Changed?.Invoke(new ClipboardSnapshot(string.Empty, sourceApp, excluded) { Image = image });

    /// <summary>Mimics a copied image without the bother of building one.</summary>
    public void EmitImage(string label, string? sourceApp = null, string? sourceExePath = null, bool excluded = false)
        => Changed?.Invoke(new ClipboardSnapshot(label, sourceApp, excluded)
        {
            SourceExePath = sourceExePath,
            Image = BlankImage.Instance,
        });

    /// <summary>Mimics a copied file selection.</summary>
    public void EmitFiles(IReadOnlyList<string> paths, string? sourceApp = null)
        => Changed?.Invoke(new ClipboardSnapshot(string.Empty, sourceApp, false)
        {
            Files = paths,
        });

    private sealed class BlankImage : IClipboardImage
    {
        public static readonly BlankImage Instance = new();

        public Task<RenderedImage> RenderAsync(CancellationToken cancellation = default)
            => Task.FromResult(new RenderedImage([1], [1], 1, 1));
    }
}
