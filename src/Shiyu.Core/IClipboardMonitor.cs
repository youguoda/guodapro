namespace Shiyu.Core;

/// <summary>
/// The clipboard half of the platform port. Core never touches the operating
/// system itself; everything arrives through here.
/// </summary>
public interface IClipboardMonitor
{
    event Action<ClipboardSnapshot>? Changed;
}
