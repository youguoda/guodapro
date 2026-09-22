namespace Shiyu.Core;

/// <summary>
/// Where full-size copied images live on disk.
///
/// On disk rather than in the database because a screenshot is megabytes and
/// the history is meant to stay small and quick; and because retention deletes
/// originals while keeping the entry, which is a file operation, not a row one.
/// </summary>
public sealed class ImageArchive(string directory)
{
    public string Directory => directory;

    /// <summary>
    /// Writes the image and returns where it went. Named by time so the folder
    /// stays browsable on its own, with a short random suffix because two
    /// copies within the same second are entirely normal.
    /// </summary>
    public string Save(byte[] png, DateTimeOffset at)
    {
        System.IO.Directory.CreateDirectory(directory);

        var name = $"{at.ToLocalTime():yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.png";
        var path = Path.Combine(directory, name);

        // Written beside the target and moved into place, so a crash midway
        // leaves no half-file that the library would show as a broken image.
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, png);
        File.Move(temporary, path, overwrite: true);

        return path;
    }

    /// <summary>
    /// Deletes an original. Missing is success: the point is that it is gone,
    /// and a user who cleared the folder by hand should not cause an error.
    /// </summary>
    public bool Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
