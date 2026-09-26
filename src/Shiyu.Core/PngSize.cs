namespace Shiyu.Core;

/// <summary>
/// Reads pixel dimensions out of a PNG header without decoding it.
///
/// The preview window wants an image's size before it loads the image (ticket
/// 17: the panel opens at its final size), and every thumbnail the store keeps
/// is a PNG whose IHDR chunk is always the first eight bytes of the payload.
/// </summary>
public static class PngSize
{
    /// <summary>
    /// The width and height in the IHDR chunk, or null when the bytes are not a
    /// PNG or are too short to hold a header.
    /// </summary>
    public static (int Width, int Height)? Read(ReadOnlySpan<byte> png)
    {
        // Signature: the eight bytes every PNG starts with.
        if (png.Length < 24
            || png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47
            || png[4] != 0x0D || png[5] != 0x0A || png[6] != 0x1A || png[7] != 0x0A)
        {
            return null;
        }

        // Then: 4-byte chunk length (13), "IHDR", then big-endian width/height.
        if (png[12] != 'I' || png[13] != 'H' || png[14] != 'D' || png[15] != 'R')
        {
            return null;
        }

        var width = ReadInt32BigEndian(png[16..20]);
        var height = ReadInt32BigEndian(png[20..24]);

        return width > 0 && height > 0 ? (width, height) : null;

        static int ReadInt32BigEndian(ReadOnlySpan<byte> bytes)
            => (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
    }
}
