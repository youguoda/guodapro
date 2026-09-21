namespace Shiyu.Core;

/// <summary>
/// What the operating system reported when the clipboard changed.
/// </summary>
/// <param name="Text">The copied text.</param>
/// <param name="SourceApp">The application that was in the foreground, if known.</param>
public sealed record ClipboardSnapshot(string Text, string? SourceApp);
