using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shiyu.Windows;

/// <summary>
/// File-type icons by extension, asked of the shell once per extension and
/// never per row — a full list would otherwise stall scrolling on system
/// calls. Uses the attribute-based lookup, so an icon is obtainable even for
/// paths that no longer exist.
/// </summary>
public sealed class FileTypeIcons
{
    private const uint ShgfiUseFileAttributes = 0x00000010;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;

    private readonly Dictionary<string, ImageSource?> _cache = [];

    /// <summary>The 16-unit icon for a path's type — its extension, or a folder.</summary>
    public ImageSource? For(string path)
    {
        var directory = Directory.Exists(path);
        var key = directory ? "\\dir" : Path.GetExtension(path).ToLowerInvariant();

        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var icon = Extract(directory, key);
        _cache[key] = icon;
        return icon;
    }

    private static ImageSource? Extract(bool directory, string extension)
    {
        try
        {
            var info = new NativeMethods.ShFileInfo();
            var result = NativeMethods.SHGetFileInfoW(
                directory ? "folder" : "file" + extension,
                directory ? FileAttributeDirectory : FileAttributeNormal,
                ref info,
                (uint)Marshal.SizeOf<NativeMethods.ShFileInfo>(),
                NativeMethods.ShgfiIcon | NativeMethods.ShgfiLargeIcon | ShgfiUseFileAttributes);

            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                NativeMethods.DestroyIcon(info.hIcon);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }
}
