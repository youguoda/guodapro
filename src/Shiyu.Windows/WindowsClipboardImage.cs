using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shiyu.Core;

namespace Shiyu.Windows;

/// <summary>
/// A bitmap taken off the clipboard, encoded on demand.
///
/// The bitmap is captured and frozen the moment the copy happens — the
/// clipboard is about to change again and there is no second chance at it —
/// but encoding, which for a full screenshot is long enough to be felt as a
/// stutter, is deferred to a background thread.
/// </summary>
public sealed class WindowsClipboardImage : IClipboardImage
{
    /// <summary>
    /// Wide enough to recognise a screenshot at a glance in the list, small
    /// enough that thousands of them stay a database worth keeping forever.
    /// </summary>
    private const int ThumbnailWidth = 240;

    private readonly BitmapSource _bitmap;

    private WindowsClipboardImage(BitmapSource bitmap) => _bitmap = bitmap;

    /// <summary>
    /// Takes the current clipboard image, or null if there is not one. Must be
    /// called on a single-threaded-apartment thread, which the message loop is.
    /// </summary>
    public static WindowsClipboardImage? FromClipboard()
    {
        try
        {
            // Applications that draw with alpha — WeChat, browsers, the
            // snipping tool — publish a PNG stream alongside the DIB, and
            // WPF's GetImage reads their DIB as solid black. The PNG is read
            // first: it is the exact image and needs no interpretation.
            if (PngFromClipboard() is { } exact)
            {
                return new WindowsClipboardImage(exact);
            }

            var bitmap = System.Windows.Clipboard.GetImage();
            if (bitmap is null)
            {
                return null;
            }

            // Frozen so it can cross to a background thread to be encoded.
            bitmap.Freeze();
            return new WindowsClipboardImage(bitmap);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another process held the clipboard. Ordinary contention.
            return null;
        }
        catch (IOException)
        {
            // A PNG stream that does not decode is not worth keeping either.
            return null;
        }
    }

    private static BitmapSource? PngFromClipboard()
    {
        if (System.Windows.Clipboard.GetData("PNG") is not { } raw)
        {
            return null;
        }

        Stream? stream = raw as Stream;
        if (raw is byte[] bytes)
        {
            stream = new MemoryStream(bytes);
        }

        if (stream is null || stream.Length == 0)
        {
            return null;
        }

        using (stream)
        {
            var decoded = BitmapFrame.Create(
                stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

            if (decoded.PixelWidth == 0 || decoded.PixelHeight == 0)
            {
                return null;
            }

            // A frame decoded from a stream keeps a live decoder that its own
            // Freeze does not cover, so encoding on the background thread
            // trips over thread affinity. Copying the bits into a
            // WriteableBitmap detaches them from the decoder entirely.
            var copy = new WriteableBitmap(decoded);
            copy.Freeze();
            return copy;
        }
    }

    public int PixelWidth => _bitmap.PixelWidth;

    public int PixelHeight => _bitmap.PixelHeight;

    /// <summary>
    /// A fingerprint of the image, used to recognise the same copy arriving
    /// twice — which it reliably does, because applications publish a bitmap in
    /// several clipboard formats in turn and each one raises a notification.
    ///
    /// Sampled rows rather than the whole bitmap: a full-screen screenshot is
    /// tens of megabytes, and allocating that on every copy to answer a
    /// yes-or-no question would be worse than the problem. Rows spread across
    /// the image make two genuinely different screenshots colliding far less
    /// likely than the duplicate this exists to catch.
    /// </summary>
    public long ContentFingerprint => _fingerprint ??= Fingerprint();

    private long? _fingerprint;

    private long Fingerprint()
    {
        const int sampleRows = 16;
        var stride = (_bitmap.PixelWidth * _bitmap.Format.BitsPerPixel + 7) / 8;
        var row = new byte[stride];

        // FNV-1a: not cryptographic, and does not need to be.
        unchecked
        {
            var hash = 14695981039346656037UL;

            void Mix(long value)
            {
                for (var i = 0; i < 8; i++)
                {
                    hash = (hash ^ (byte)(value >> (i * 8))) * 1099511628211UL;
                }
            }

            Mix(_bitmap.PixelWidth);
            Mix(_bitmap.PixelHeight);

            var step = Math.Max(1, _bitmap.PixelHeight / sampleRows);
            for (var y = 0; y < _bitmap.PixelHeight; y += step)
            {
                _bitmap.CopyPixels(new System.Windows.Int32Rect(0, y, _bitmap.PixelWidth, 1), row, stride, 0);
                foreach (var value in row)
                {
                    hash = (hash ^ value) * 1099511628211UL;
                }
            }

            return (long)hash;
        }
    }

    public Task<RenderedImage> RenderAsync(CancellationToken cancellation = default)
        => Task.Run(() =>
        {
            cancellation.ThrowIfCancellationRequested();

            var full = Encode(_bitmap);
            var thumbnail = Encode(Shrink(_bitmap, ThumbnailWidth));

            return new RenderedImage(full, thumbnail, _bitmap.PixelWidth, _bitmap.PixelHeight);
        }, cancellation);

    private static BitmapSource Shrink(BitmapSource source, int targetWidth)
    {
        if (source.PixelWidth <= targetWidth)
        {
            return source;
        }

        var scale = (double)targetWidth / source.PixelWidth;
        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    private static byte[] Encode(BitmapSource bitmap)
    {
        // PNG rather than JPEG: screenshots are mostly text and flat colour,
        // where JPEG's artefacts land exactly on the parts worth reading.
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        return buffer.ToArray();
    }
}
