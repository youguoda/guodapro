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

    public void Emit(string text, string? sourceApp = null)
        => Changed?.Invoke(new ClipboardSnapshot(text, sourceApp));
}
