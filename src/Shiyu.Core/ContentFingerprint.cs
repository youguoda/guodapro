using System.Security.Cryptography;
using System.Text;

namespace Shiyu.Core;

/// <summary>
/// What makes two entries the same entry. Time differs between machines and
/// sources come and go, but the content is the content — so merge-import
/// decides "already have it" here, never on a timestamp.
/// </summary>
public static class ContentFingerprint
{
    /// <summary>
    /// A stable hex digest of the entry's substance: its kind plus the bytes
    /// that make it what it is. The thumbnail stands in for an image — it is
    /// what the history durably keeps, and two copies of one screenshot are
    /// one copy however many times it was republished.
    /// </summary>
    public static string Of(Entry entry)
    {
        using var sha = SHA256.Create();
        var digest = sha.ComputeHash(PayloadOf(entry));
        return Convert.ToHexString(digest);
    }

    internal static bool SameContent(Entry left, Entry right)
        => left.Kind == right.Kind && Of(left) == Of(right);

    private static byte[] PayloadOf(Entry entry) => entry.Kind switch
    {
        // Formatting is substance: a rich copy and its bare text are not the
        // same entry, and a merge that treated them so would quietly lose one.
        EntryKind.Text => Concat(
            [(byte)EntryKind.Text],
            Encoding.UTF8.GetBytes(entry.Text ?? string.Empty),
            Encoding.UTF8.GetBytes(entry.Html ?? string.Empty),
            Encoding.UTF8.GetBytes(entry.Rtf ?? string.Empty)),

        EntryKind.Files => Concat(
            [(byte)EntryKind.Files],
            Encoding.UTF8.GetBytes(string.Join("\n", entry.Files))),

        // Text is the human stand-in ("图片 545×444"), not the picture.
        _ => Concat([(byte)EntryKind.Image], entry.ThumbnailPng ?? []),
    };

    private static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(part => part.Length);
        var merged = new byte[total];
        var at = 0;
        foreach (var part in parts)
        {
            part.CopyTo(merged, at);
            at += part.Length;
        }

        return merged;
    }
}
