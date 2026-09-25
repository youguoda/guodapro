using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shiyu.Core;

/// <summary>Everything the user needs to know when a backup cannot proceed.</summary>
public sealed class BackupException(string message) : Exception(message);

public sealed record BackupCounts(int Entries, int Images, long Bytes);

public sealed record BackupImportResult(int Added, int SkippedExisting, int GroupsCreated, int ImagesRestored);

/// <summary>
/// One file holding the whole history: entries with every marker, groups,
/// the image originals still on disk, and the settings — optionally sealed
/// under a password.
///
/// The database stays unencrypted on purpose; a backup is different — it
/// leaves the machine, onto drives and clouds the history never visits, and
/// there "nothing to protect" stops being true. Password derivation is
/// PBKDF2-HMAC-SHA256 at 210,000 rounds; the body is AES-GCM in chunks, so
/// gigabyte archives stream without ever sitting whole in memory, and a
/// wrong password fails the first chunk's tag instead of decrypting garbage.
/// </summary>
public static class BackupArchive
{
    public const int KdfIterations = 210_000;

    private const int ChunkSize = 4 * 1024 * 1024;

    private static readonly byte[] Magic = "SHIYUBK1"u8.ToArray();

    public sealed record EntryDto(
        string Text,
        string? SourceApp,
        long CreatedAtMs,
        int Kind,
        string? Thumbnail,
        string? OriginalImage,
        string? Subtype,
        string? Html,
        string? Rtf,
        string[] Files,
        bool Pinned,
        bool Favorite,
        string? Note,
        int UseCount,
        string[] Tags,
        string? Group);

    public sealed record GroupDto(string Name, string? Icon, int Position, bool Hidden);

    public sealed record ManifestDto(int Format, long CreatedAtMs, int Entries, int Images, bool Encrypted);

    /// <summary>
    /// Writes the archive. <paramref name="passwordUtf16"/> is the password
    /// as UTF-16LE bytes — the caller keeps it in no string — and is wiped
    /// here, the only place it is ever read.
    /// </summary>
    public static BackupCounts Export(
        string targetPath,
        EntryStore store,
        string imagesDirectory,
        string? settingsJson,
        byte[]? passwordUtf16,
        IProgress<string>? progress = null)
    {
        try
        {
            var staging = targetPath + ".staging";
            try
            {
                int entries;
                int images;
                using (var zip = ZipFile.Open(staging, ZipArchiveMode.Create))
                {
                    (entries, images) = WriteContents(zip, store, imagesDirectory, settingsJson, progress);
                    var manifest = new ManifestDto(
                        1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), entries, images,
                        passwordUtf16 is not null);
                    WriteJson(zip, "manifest.json", manifest);
                }

                // Only now, with the archive closed, does the staging file
                // move or get sealed — a handle still open would lock it.
                if (passwordUtf16 is null)
                {
                    File.Move(staging, targetPath, overwrite: true);
                }
                else
                {
                    Seal(staging, targetPath, passwordUtf16);
                }

                return new BackupCounts(entries, images, new FileInfo(targetPath).Length);
            }
            finally
            {
                if (File.Exists(staging))
                {
                    File.Delete(staging);
                }
            }
        }
        finally
        {
            if (passwordUtf16 is not null)
            {
                Array.Clear(passwordUtf16);
            }
        }
    }

    private static (int Entries, int Images) WriteContents(
        ZipArchive zip, EntryStore store, string imagesDirectory, string? settingsJson, IProgress<string>? progress)
    {
        var groups = store.Groups()
            .Select(group => new GroupDto(group.Name, group.Icon, group.Position, group.Hidden))
            .ToList();
        WriteJson(zip, "groups.json", groups);

        if (settingsJson is not null)
        {
            var entry = zip.CreateEntry("settings.json");
            using var raw = entry.Open();
            raw.Write(Encoding.UTF8.GetBytes(settingsJson));
        }

        var dtos = new List<EntryDto>();
        var imageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var done = 0;

        while (true)
        {
            var page = store.Page(200, dtos.Count);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var entry in page)
            {
                var original = entry.OriginalPath is { Length: > 0 } path && File.Exists(path)
                    ? Path.GetFileName(path)
                    : null;
                if (original is not null && imageNames.Add(original))
                {
                    zip.CreateEntryFromFile(
                        Path.Combine(imagesDirectory, original), "images/" + original,
                        CompressionLevel.Optimal);
                }

                dtos.Add(new EntryDto(
                    entry.Text,
                    entry.SourceApp,
                    entry.CreatedAt.ToUnixTimeMilliseconds(),
                    (int)entry.Kind,
                    entry.ThumbnailPng is { } thumb ? Convert.ToBase64String(thumb) : null,
                    original,
                    entry.Subtype == EntrySubtype.None ? null : entry.Subtype.ToString(),
                    entry.Html,
                    entry.Rtf,
                    [.. entry.Files],
                    entry.IsPinned,
                    entry.Favorite,
                    entry.Note,
                    entry.UseCount,
                    [.. entry.Tags],
                    store.GroupOf(entry)?.Name));
            }

            done += page.Count;
            progress?.Report($"已收集 {done} 条");
        }

        WriteJson(zip, "entries.json", dtos);
        return (dtos.Count, imageNames.Count);
    }

    /// <summary>
    /// Reads an archive into the store. Overwrite replaces the history (and
    /// applies the archived settings); merge keeps everything present and
    /// adds only what is missing, decided by content fingerprint — the same
    /// entry on two machines has two timestamps but one content.
    /// </summary>
    public static BackupImportResult Import(
        string backupPath,
        EntryStore store,
        string imagesDirectory,
        bool overwrite,
        Action<string>? applySettings,
        byte[]? passwordUtf16,
        IProgress<string>? progress = null)
    {
        try
        {
            var (plain, isTemporary) = OpenPlainOrUnseal(backupPath, passwordUtf16);
            try
            {
                return ReadInto(plain, store, imagesDirectory, overwrite, applySettings, progress);
            }
            finally
            {
                // The user's backup file must never be tidied away by its own
                // reader — only the decrypted scratch copy goes.
                if (isTemporary)
                {
                    File.Delete(plain);
                }
            }
        }
        finally
        {
            if (passwordUtf16 is not null)
            {
                Array.Clear(passwordUtf16);
            }
        }
    }

    private static (string Path, bool Temporary) OpenPlainOrUnseal(string backupPath, byte[]? passwordUtf16)
    {
        using var probe = File.OpenRead(backupPath);
        var head = new byte[4];
        var read = probe.Read(head, 0, 4);

        // "PK\x03\x04" — an unencrypted archive is just the zip.
        if (read == 4 && head[0] == 'P' && head[1] == 'K')
        {
            return (backupPath, false);
        }

        probe.Seek(0, SeekOrigin.Begin);
        var magic = new byte[Magic.Length];
        try
        {
            probe.ReadExactly(magic);
        }
        catch (EndOfStreamException)
        {
            throw new BackupException("备份文件不完整。");
        }

        if (!magic.SequenceEqual(Magic))
        {
            // Known before the password is asked for: a file that is neither
            // a zip nor a sealed archive is not ours, whatever the password.
            throw new BackupException("这不是拾语的备份文件。");
        }

        if (passwordUtf16 is null)
        {
            throw new BackupException("这个备份加了口令，请输入口令后重试。");
        }

        try
        {
            var salt = new byte[16];
            probe.ReadExactly(salt);
            var iterations = BitConverter.ToInt32(ReadExactly(probe, 4));
            if (iterations is < 1 or > 10_000_000)
            {
                throw new BackupException("备份文件的加密参数不合法，可能已损坏。");
            }

            var plainPath = Path.GetTempFileName();
            try
            {
                using var output = File.Create(plainPath);
                using var derive = new Rfc2898DeriveBytes(passwordUtf16, salt, iterations, HashAlgorithmName.SHA256);
                using var aes = new AesGcm(derive.GetBytes(32), 16);

                // Each chunk's ciphertext carries its own 16-byte tag after
                // the payload, so the buffer holds both.
                var chunk = new byte[ChunkSize + 16];
                while (true)
                {
                    var nonce = new byte[12];
                    var got = probe.Read(nonce, 0, 12);
                    if (got == 0)
                    {
                        break;
                    }

                    if (got < 12)
                    {
                        throw new BackupException("备份文件不完整。");
                    }

                    var loaded = ReadUpTo(probe, chunk, chunk.Length);
                    if (loaded <= 16)
                    {
                        throw new BackupException("备份文件不完整。");
                    }

                    try
                    {
                        var body = loaded - 16;
                        var plain = new byte[body];
                        aes.Decrypt(
                            nonce,
                            chunk.AsSpan(0, body),
                            chunk.AsSpan(body, 16),
                            plain);
                        output.Write(plain);
                    }
                    catch (CryptographicException)
                    {
                        throw new BackupException("口令不正确，或备份文件已损坏。");
                    }
                }

                return (plainPath, true);
            }
            catch
            {
                File.Delete(plainPath);
                throw;
            }
        }
        catch (EndOfStreamException)
        {
            throw new BackupException("备份文件不完整。");
        }
    }

    private static BackupImportResult ReadInto(
        string zipPath, EntryStore store, string imagesDirectory, bool overwrite, Action<string>? applySettings, IProgress<string>? progress)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        var manifest = zip.GetEntry("manifest.json") is { } manifestEntry
            ? ReadJson<ManifestDto>(manifestEntry)
            : null;
        if (manifest is null || manifest.Format != 1)
        {
            throw new BackupException("备份格式不认识——请用更新版本的拾语打开。");
        }

        var groups = zip.GetEntry("groups.json") is { } groupsEntry
            ? ReadJson<List<GroupDto>>(groupsEntry) ?? []
            : [];

        if (overwrite)
        {
            store.ClearAll();
        }

        // The groups land before the entries, in the order the archive kept:
        // CreateGroup appends, so their positions carry over unchanged.
        var groupCount = 0;
        foreach (var group in groups)
        {
            if (store.Groups().All(have => have.Name != group.Name))
            {
                var id = store.CreateGroup(group.Name, group.Icon);
                if (group.Hidden)
                {
                    store.SetGroupHidden(id, true);
                }

                groupCount++;
            }
        }

        if (overwrite && zip.GetEntry("settings.json") is { } settingsEntry)
        {
            using var reader = new StreamReader(settingsEntry.Open(), Encoding.UTF8);
            applySettings?.Invoke(reader.ReadToEnd());
        }

        var existing = new HashSet<string>();
        if (!overwrite)
        {
            var offset = 0;
            while (true)
            {
                var page = store.Page(500, offset);
                if (page.Count == 0)
                {
                    break;
                }

                foreach (var entry in page)
                {
                    existing.Add(ContentFingerprint.Of(entry));
                }

                offset += page.Count;
            }
        }

        var entriesEntry = zip.GetEntry("entries.json") ?? throw new BackupException("备份里没有条目数据，可能已损坏。");
        var dtos = ReadJson<List<EntryDto>>(entriesEntry) ?? [];

        Directory.CreateDirectory(imagesDirectory);
        var added = 0;
        var skipped = 0;
        var images = 0;

        foreach (var dto in dtos)
        {
            var entry = AsEntry(dto, out var carried);
            if (!overwrite && !existing.Add(ContentFingerprint.Of(entry)))
            {
                skipped++;
                continue;
            }

            if (carried is not null)
            {
                var target = Path.Combine(imagesDirectory, Path.GetFileName(carried));
                if (!File.Exists(target)
                    && zip.GetEntry("images/" + Path.GetFileName(carried)) is { } image)
                {
                    image.ExtractToFile(target, overwrite: false);
                    images++;
                }

                entry = entry with { OriginalPath = target };
            }

            store.ImportEntry(entry, dto.Group);
            added++;

            if (added % 200 == 0)
            {
                progress?.Report($"已导入 {added} 条");
            }
        }

        return new BackupImportResult(added, skipped, groupCount, images);
    }

    private static Entry AsEntry(EntryDto dto, out string? carried)
    {
        carried = dto.OriginalImage;
        return new Entry(0, dto.Text, dto.SourceApp, DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAtMs))
        {
            Kind = (EntryKind)dto.Kind,
            ThumbnailPng = dto.Thumbnail is { } thumb ? Convert.FromBase64String(thumb) : null,
            OriginalPath = dto.OriginalImage,
            IsPinned = dto.Pinned,
            Subtype = dto.Subtype is null ? EntrySubtype.None
                : Enum.TryParse<EntrySubtype>(dto.Subtype, out var parsed) ? parsed : EntrySubtype.None,
            Html = dto.Html,
            Rtf = dto.Rtf,
            Files = dto.Files,
            Favorite = dto.Favorite,
            Note = dto.Note,
            UseCount = dto.UseCount,
            Tags = dto.Tags,
        };
    }

    private static void Seal(string plainPath, string targetPath, byte[] passwordUtf16)
    {
        using var plain = File.OpenRead(plainPath);
        using var output = File.Create(targetPath);
        var salt = RandomNumberGenerator.GetBytes(16);

        output.Write(Magic);
        output.Write(salt);
        output.Write(BitConverter.GetBytes(KdfIterations));

        using var derive = new Rfc2898DeriveBytes(passwordUtf16, salt, KdfIterations, HashAlgorithmName.SHA256);
        using var aes = new AesGcm(derive.GetBytes(32), 16);

        var buffer = new byte[ChunkSize];
        while (true)
        {
            var loaded = ReadUpTo(plain, buffer, ChunkSize);
            if (loaded == 0)
            {
                break;
            }

            var nonce = RandomNumberGenerator.GetBytes(12);
            output.Write(nonce);
            var sealedChunk = new byte[loaded + 16];
            aes.Encrypt(
                nonce,
                buffer.AsSpan(0, loaded),
                sealedChunk.AsSpan(0, loaded),
                sealedChunk.AsSpan(loaded, 16),
                associatedData: null);
            output.Write(sealedChunk);
        }
    }

    private static void WriteJson<T>(ZipArchive zip, string name, T value)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, value);
    }

    private static T? ReadJson<T>(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return JsonSerializer.Deserialize<T>(stream);
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static int ReadUpTo(Stream stream, byte[] buffer, int count)
    {
        var total = 0;
        while (total < count)
        {
            var got = stream.Read(buffer, total, count - total);
            if (got == 0)
            {
                break;
            }

            total += got;
        }

        return total;
    }
}
