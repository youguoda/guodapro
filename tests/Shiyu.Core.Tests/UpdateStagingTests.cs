using System.IO.Compression;
using System.Security.Cryptography;
using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>A disposable empty directory, for the file-shaped tests.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "shiyu-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { }
    }
}

public class UpdateStagingTests
{
    private static byte[] ZipWithApp(Action<ZipArchive>? extra = null)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("Shiyu.App.exe");
            using (var writer = new StreamWriter(entry.Open()))
            {
                writer.Write("new exe");
            }

            extra?.Invoke(zip);
        }

        return stream.ToArray();
    }

    [Fact]
    public void A_verified_installer_passes_size_hash_and_content_checks()
    {
        using var directory = new TempDirectory();
        var bytes = ZipWithApp();
        var path = Path.Combine(directory.Path, "installer.zip");
        File.WriteAllBytes(path, bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var (ok, error) = UpdateStaging.VerifyInstaller(path, bytes.Length, hash);

        Assert.True(ok, error);

        // And the checksum asset is optional, not mandatory.
        var (okBlind, _) = UpdateStaging.VerifyInstaller(path, bytes.Length, null);
        Assert.True(okBlind);
    }

    [Fact]
    public void A_size_mismatch_is_rejected_with_something_to_say()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "installer.zip");
        File.WriteAllBytes(path, ZipWithApp());

        var (ok, error) = UpdateStaging.VerifyInstaller(path, expectedSize: 1, expectedSha256Hex: null);

        Assert.False(ok);
        Assert.Contains("大小", error);
    }

    [Fact]
    public void A_hash_mismatch_is_rejected()
    {
        using var directory = new TempDirectory();
        var bytes = ZipWithApp();
        var path = Path.Combine(directory.Path, "installer.zip");
        File.WriteAllBytes(path, bytes);

        var (ok, error) = UpdateStaging.VerifyInstaller(path, bytes.Length, "deadbeef");

        Assert.False(ok);
        Assert.Contains("SHA256", error);
    }

    [Fact]
    public void Truncated_installer_bytes_are_rejected()
    {
        using var directory = new TempDirectory();
        var bytes = ZipWithApp()[..^10];
        var path = Path.Combine(directory.Path, "installer.zip");
        File.WriteAllBytes(path, bytes);

        var (ok, error) = UpdateStaging.VerifyInstaller(path, bytes.Length, null);

        Assert.False(ok);
    }

    [Fact]
    public void A_zip_without_the_application_is_not_an_installer()
    {
        using var directory = new TempDirectory();
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("readme.txt");
        }

        var path = Path.Combine(directory.Path, "installer.zip");
        File.WriteAllBytes(path, stream.ToArray());

        var (ok, error) = UpdateStaging.VerifyInstaller(path, (int)stream.Length, null);

        Assert.False(ok);
        Assert.Contains("Shiyu.App.exe", error);
    }

    [Fact]
    public void Pending_round_trips_and_resets_cleanly()
    {
        using var directory = new TempDirectory();
        var data = directory.Path;

        Assert.Null(UpdateStaging.ReadPending(data));

        UpdateStaging.WritePending(
            data,
            new ReleaseManifest(new UpdateVersion(1, 4, 2), "说明\n第二行", DateTimeOffset.Now, []));

        var pending = UpdateStaging.ReadPending(data);
        Assert.NotNull(pending);
        Assert.Equal("1.4.2", pending.Version);
        Assert.Contains("第二行", pending.Notes);

        UpdateStaging.Reset(data);
        Assert.Null(UpdateStaging.ReadPending(data));
        Assert.False(Directory.Exists(UpdateStaging.Root(data)));
    }

    [Fact]
    public void Extract_lands_the_whole_tree_in_a_clean_directory()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "installer.zip");
        File.WriteAllBytes(path, ZipWithApp(zip =>
            zip.CreateEntry("runtimes/win/lib/net9.0/dependency.dll")));

        var staged = Path.Combine(directory.Path, "staged");
        UpdateStaging.Extract(path, staged);

        Assert.True(File.Exists(Path.Combine(staged, "Shiyu.App.exe")));
        Assert.True(File.Exists(Path.Combine(staged, "runtimes/win/lib/net9.0/dependency.dll")));
    }
}
