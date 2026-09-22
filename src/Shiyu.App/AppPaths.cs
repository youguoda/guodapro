using System.IO;

namespace Shiyu.App;

/// <summary>
/// Where Shiyu keeps its data.
///
/// LocalApplicationData by default, chosen deliberately: the history is not
/// encrypted, so "the data does not leave this machine" rests entirely on
/// storing it somewhere that does not roam and that sync clients do not touch.
/// The user can move it, and is warned if they pick a synced folder.
/// </summary>
internal static class AppPaths
{
    // Declared before anything that reads it: static initialisers run in
    // declaration order, and the other way round leaves the root null.
    internal static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Shiyu");

    private static string _root = DefaultRoot;

    internal static string DataDirectory => _root;

    internal static string DatabaseFile => Path.Combine(_root, "history.db");

    internal static string ImageDirectory => Path.Combine(_root, "images");

    /// <summary>
    /// The settings file always lives in the default location, never in the
    /// overridden one — otherwise moving the data would hide the setting that
    /// says where the data went.
    /// </summary>
    internal static string SettingsFile { get; } = Path.Combine(DefaultRoot, "settings.json");

    /// <summary>Points the data paths elsewhere. Read once, at startup.</summary>
    internal static void UseDirectory(string? directory)
        => _root = string.IsNullOrWhiteSpace(directory) ? DefaultRoot : directory.Trim();
}
