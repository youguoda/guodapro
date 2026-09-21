namespace Shiyu.Core;

/// <summary>
/// What the operating system reported when the clipboard changed.
/// </summary>
/// <param name="Text">The copied text.</param>
/// <param name="SourceApp">The application that was in the foreground, if known.</param>
/// <param name="ExcludedByMarker">
/// Whether the copying application asked clipboard tools to leave this content
/// alone. Detecting the request belongs to the platform; deciding what to do
/// about it belongs to <see cref="ExclusionPolicy"/>.
/// </param>
public sealed record ClipboardSnapshot(string Text, string? SourceApp, bool ExcludedByMarker);
