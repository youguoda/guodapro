using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// 编排的可测骨架（票 09）：一个按路径回放固定文件的假发布源，一把测试
/// 自造的签名钥。这里钉住的是分叉——每个"不"都必须既报中文人话、又把
/// 暂存区清空。
/// </summary>
public class UpdateOrchestratorTests
{
    private static readonly UpdateChannel Channel = new("example", "shiyu", "shiyu-win-x64.zip");

    private static byte[] InstallerZip()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("Shiyu.App.exe");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("new exe");
        }

        return stream.ToArray();
    }

    private static (byte[] Signature, string PublicKeyPem) Sign(byte[] package)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (
            key.SignData(package, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence),
            key.ExportSubjectPublicKeyInfoPem());
    }

    /// <summary>一条完整的发布：安装包、签名、校验和三件齐全，互相吻合。</summary>
    private static ReleaseManifest Release(long installerSize)
        => new(
            new UpdateVersion(1, 4, 2),
            "修复了若干问题。",
            DateTimeOffset.Now,
            [
                new ReleaseAsset("shiyu-win-x64.zip", installerSize, "https://example.invalid/shiyu-win-x64.zip"),
                new ReleaseAsset("shiyu-win-x64.zip.sig", 72, "https://example.invalid/shiyu-win-x64.zip.sig"),
                new ReleaseAsset("shiyu-win-x64.zip.sha256", 90, "https://example.invalid/shiyu-win-x64.zip.sha256"),
            ]);

    private static string ChecksumText(byte[] package)
        => Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant() + "  shiyu-win-x64.zip";

    private static UpdateOrchestrator Orchestrator(
        FakeReleaseServer server, string dataDirectory, string publicKeyPem)
        => new(new HttpClient(server), dataDirectory, Channel, publicKeyPem);

    [Fact]
    public async Task The_whole_pipeline_stages_a_signed_release()
    {
        using var area = new TempDirectory();
        var package = InstallerZip();
        var (signature, publicKey) = Sign(package);
        using var server = new FakeReleaseServer(new Dictionary<string, object>
        {
            ["/shiyu-win-x64.zip"] = package,
            ["/shiyu-win-x64.zip.sig"] = signature,
            ["/shiyu-win-x64.zip.sha256"] = ChecksumText(package),
        });

        var (ok, error) = await Orchestrator(server, area.Path, publicKey).DownloadAsync(
            Release(package.Length), progress: null, CancellationToken.None);

        Assert.True(ok, error);
        Assert.True(File.Exists(
            Path.Combine(UpdateStaging.StagedDirectory(area.Path), "Shiyu.App.exe")));
        Assert.Equal("1.4.2", UpdateStaging.ReadPending(area.Path)?.Version);
    }

    [Fact]
    public async Task A_tampered_package_is_rejected_and_the_staging_area_left_empty()
    {
        using var area = new TempDirectory();
        var package = InstallerZip();
        var (signature, publicKey) = Sign(package);

        // 中间人改了一个字节：签名对的是原包，对不上眼前的包。
        package[^1] ^= 1;
        using var server = new FakeReleaseServer(new Dictionary<string, object>
        {
            ["/shiyu-win-x64.zip"] = package,
            ["/shiyu-win-x64.zip.sig"] = signature,
            ["/shiyu-win-x64.zip.sha256"] = ChecksumText(package),
        });

        var (ok, error) = await Orchestrator(server, area.Path, publicKey).DownloadAsync(
            Release(package.Length), progress: null, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("签名不符", error);
        Assert.False(Directory.Exists(UpdateStaging.Root(area.Path)));
    }

    [Fact]
    public async Task A_release_without_a_signature_asset_is_refused()
    {
        using var area = new TempDirectory();
        var package = InstallerZip();
        var (_, publicKey) = Sign(package);
        var release = Release(package.Length) with
        {
            Assets = [.. Release(package.Length).Assets.Where(asset => !asset.Name.EndsWith(".sig"))],
        };
        using var server = new FakeReleaseServer(new Dictionary<string, object>
        {
            ["/shiyu-win-x64.zip"] = package,
        });

        var (ok, error) = await Orchestrator(server, area.Path, publicKey).DownloadAsync(
            release, progress: null, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("签名", error);
        Assert.False(Directory.Exists(UpdateStaging.Root(area.Path)));
    }

    [Fact]
    public async Task A_signature_that_cannot_be_downloaded_is_refused()
    {
        using var area = new TempDirectory();
        var package = InstallerZip();
        var (signature, publicKey) = Sign(package);
        using var server = new FakeReleaseServer(new Dictionary<string, object>
        {
            ["/shiyu-win-x64.zip"] = package,
            ["/shiyu-win-x64.zip.sha256"] = ChecksumText(package),
            // .sig 没有路由 → 404：取不到签名与没有签名同一个下场。
        });

        var (ok, error) = await Orchestrator(server, area.Path, publicKey).DownloadAsync(
            Release(package.Length), progress: null, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("下载失败", error);
        Assert.False(Directory.Exists(UpdateStaging.Root(area.Path)));
    }

    [Fact]
    public async Task A_checksum_that_cannot_be_fetched_is_no_longer_waved_through()
    {
        using var area = new TempDirectory();
        var package = InstallerZip();
        var (signature, publicKey) = Sign(package);
        using var server = new FakeReleaseServer(new Dictionary<string, object>
        {
            ["/shiyu-win-x64.zip"] = package,
            ["/shiyu-win-x64.zip.sig"] = signature,
            // .sha256 没有路由 → 404：从前的 fail-open 会当它不存在。
        });

        var (ok, error) = await Orchestrator(server, area.Path, publicKey).DownloadAsync(
            Release(package.Length), progress: null, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("下载失败", error);
        Assert.False(Directory.Exists(UpdateStaging.Root(area.Path)));
    }

    [Fact]
    public async Task An_installer_download_failure_cleans_the_staging_area()
    {
        using var area = new TempDirectory();
        var package = InstallerZip();
        var (signature, publicKey) = Sign(package);
        using var server = new FakeReleaseServer([]);

        var (ok, error) = await Orchestrator(server, area.Path, publicKey).DownloadAsync(
            Release(package.Length), progress: null, CancellationToken.None);

        Assert.False(ok);
        Assert.False(Directory.Exists(UpdateStaging.Root(area.Path)));
    }

    [Fact]
    public async Task A_cancelled_download_cleans_the_staging_area_and_says_so()
    {
        using var area = new TempDirectory();
        var package = InstallerZip();
        var (signature, publicKey) = Sign(package);
        using var server = new FakeReleaseServer(new Dictionary<string, object>
        {
            ["/shiyu-win-x64.zip"] = package,
            ["/shiyu-win-x64.zip.sig"] = signature,
            ["/shiyu-win-x64.zip.sha256"] = ChecksumText(package),
        });
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var (ok, error) = await Orchestrator(server, area.Path, publicKey).DownloadAsync(
            Release(package.Length), progress: null, cancelled.Token);

        Assert.False(ok);
        Assert.Contains("取消", error);
        Assert.False(Directory.Exists(UpdateStaging.Root(area.Path)));
    }

    [Fact]
    public async Task CheckAsync_reads_the_latest_release_from_the_channel()
    {
        using var server = new FakeReleaseServer(new Dictionary<string, object>
        {
            ["/repos/example/shiyu/releases/latest"] = """
                {
                  "tag_name": "v1.4.2",
                  "body": "说明",
                  "assets": [
                    { "name": "shiyu-win-x64.zip", "size": 10, "browser_download_url": "https://example.invalid/shiyu-win-x64.zip" },
                    { "name": "shiyu-win-x64.zip.sig", "size": 72, "browser_download_url": "https://example.invalid/shiyu-win-x64.zip.sig" }
                  ]
                }
                """,
        });

        var release = await new UpdateOrchestrator(
            new HttpClient(server), Path.GetTempPath(), Channel).CheckAsync();

        Assert.NotNull(release);
        Assert.Equal(new UpdateVersion(1, 4, 2), release.Version);
        Assert.NotNull(release.SignatureFor("shiyu-win-x64.zip"));
    }

    /// <summary>
    /// 按请求路径回放固定文件的假发布源：byte[] 按二进制、string 按文本，
    /// 没有路由一律 404。路径匹配不看主机——编排只该关心它要哪个文件。
    /// </summary>
    private sealed class FakeReleaseServer(Dictionary<string, object> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellation)
        {
            if (!files.TryGetValue(request.RequestUri!.AbsolutePath, out var file))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            HttpContent content = file switch
            {
                byte[] bytes => new ByteArrayContent(bytes),
                string text => new StringContent(text),
                _ => throw new InvalidOperationException("unknown fixture"),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
