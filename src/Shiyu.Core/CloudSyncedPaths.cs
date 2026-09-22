namespace Shiyu.Core;

/// <summary>
/// Recognises folders that a sync client is likely to copy off this machine.
///
/// This matters more here than it would in most applications. The history is
/// not encrypted, and the promise Shiyu makes is that it stays on this machine.
/// Putting the database inside a synced folder breaks that promise quietly:
/// nothing fails, nothing looks different, and the plaintext history is on
/// somebody's servers.
/// </summary>
public static class CloudSyncedPaths
{
    /// <summary>
    /// Folder names that mean "this is synced". Matched as a whole path
    /// segment, so an ordinary folder that merely contains one of these words —
    /// "OneDrive迁移笔记" — is not flagged.
    /// </summary>
    private static readonly string[] SyncedFolders =
    [
        "OneDrive",
        "Dropbox",
        "Google Drive",
        "GoogleDrive",
        "iCloudDrive",
        "iCloud Drive",
        "Nextcloud",
        "ownCloud",
        "Syncthing",
        "坚果云",
        "百度网盘",
        "BaiduNetdiskDownload",
        "WPS Cloud Files",
        "WPS网盘",
        "MEGAsync",
        "pCloudDrive",
    ];

    /// <summary>
    /// Returns which sync folder the path sits in, or null if none does.
    /// Returning the name rather than a yes-or-no so the warning can say what
    /// it found — a vague "this might be synced" is easy to dismiss.
    /// </summary>
    public static string? DetectSyncFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var segment in segments)
        {
            foreach (var folder in SyncedFolders)
            {
                // Prefix rather than equality: OneDrive's business folders are
                // named "OneDrive - Contoso" and sync just as eagerly.
                if (segment.Equals(folder, StringComparison.OrdinalIgnoreCase)
                    || segment.StartsWith(folder + " - ", StringComparison.OrdinalIgnoreCase))
                {
                    return segment;
                }
            }
        }

        return null;
    }

    public static bool LooksSynced(string path) => DetectSyncFolder(path) is not null;
}
