using System.IO;

namespace Shiyu.App;

/// <summary>
/// Where Shiyu keeps its data.
///
/// LocalApplicationData is chosen deliberately: the history is not encrypted,
/// so "the data does not leave this machine" rests entirely on storing it
/// somewhere that does not roam and that OneDrive does not sync by default.
/// </summary>
internal static class AppPaths
{
    internal static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Shiyu");

    internal static string DatabaseFile { get; } = Path.Combine(DataDirectory, "history.db");

    internal static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");
}
