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
    /// says where the data went. (The one exception is a probe directory;
    /// see <see cref="UseProbeDirectory"/>.)
    /// </summary>
    internal static string SettingsFile => _settingsFile;

    private static string _settingsFile = Path.Combine(DefaultRoot, "settings.json");

    /// <summary>Points the data paths elsewhere. Read once, at startup.</summary>
    internal static void UseDirectory(string? directory)
        => _root = string.IsNullOrWhiteSpace(directory) ? DefaultRoot : directory.Trim();

#if DEBUG
    /// <summary>
    /// A probe instance's whole world is the isolated directory (ticket 15) —
    /// including the settings file, which UseDirectory deliberately leaves in
    /// the default location. Without this, a probe would load the user's real
    /// settings and, worse, write them back (geometry saves, the relay client
    /// id) from under the instance the user is actually running.
    /// Debug builds only; a release build has no probe mode.
    /// </summary>
    internal static void UseProbeDirectory(string directory)
    {
        UseDirectory(directory);
        _settingsFile = Path.Combine(_root, "settings.json");
    }
#endif
}
