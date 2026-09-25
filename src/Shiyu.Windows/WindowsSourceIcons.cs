using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Shiyu.Core;

namespace Shiyu.Windows;

/// <summary>
/// Extracts an application's icon the way Explorer shows it, encoded to PNG
/// once and never asked for again while the application lives.
///
/// Called on the message-loop thread, which is the single-threaded apartment
/// the icon-to-bitmap conversion requires.
/// </summary>
public sealed class WindowsSourceIcons : ISourceIconProvider
{
    public byte[]? ExtractIconPng(string exePath)
    {
        try
        {
            var info = new NativeMethods.ShFileInfo();
            var result = NativeMethods.SHGetFileInfoW(
                exePath,
                0,
                ref info,
                (uint)Marshal.SizeOf<NativeMethods.ShFileInfo>(),
                NativeMethods.ShgfiIcon | NativeMethods.ShgfiLargeIcon);

            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using var stream = new MemoryStream();
                encoder.Save(stream);
                return stream.ToArray();
            }
            finally
            {
                NativeMethods.DestroyIcon(info.hIcon);
            }
        }
        catch (Exception)
        {
            // A missing or odd icon is the normal case, not an error: the
            // cache stores a tombstone and the list shows Shiyu's own mark.
            return null;
        }
    }
}
