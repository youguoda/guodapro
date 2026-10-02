using System.Net.Http;

namespace Shiyu.Core;

/// <summary>
/// 更新的编排（票 09 / ADR-0010）：取清单 → 选资产 → 下载 → 下载 .sig 并
/// 验签 → 校验大小与 SHA-256（只管完整性，不再承担信任）→ 解压 → 写
/// pending。整个流程在 Core 里跑，HttpClient 与数据目录从外面注入，网络
/// 的每一个分叉都可以在测试里钉死。
///
/// 信任只有一道门：内置公钥对安装包完整字节的验签。没有 .sig 资产、
/// 签名下不来、验签不过——一律拒绝并清空暂存区，不存在任何"取不到就
/// 放行"的旁路；发布带了 .sha256 就必须下得来且对得上（从前的校验和
/// fail-open 已删）。
/// </summary>
public sealed class UpdateOrchestrator(
    HttpClient http,
    string dataDirectory,
    UpdateChannel? channel = null,
    string? publicKeyPem = null,
    string? userAgent = null)
{
    /// <summary>调试构建专用的发布源覆盖：SHIYU_UPDATE_API。发布构建不认它（O-06）。</summary>
    private const string ApiOverrideVariable = "SHIYU_UPDATE_API";

    private readonly HttpClient _http = http;
    private readonly string _dataDirectory = dataDirectory;
    private readonly UpdateChannel _channel = channel ?? UpdateChannel.Default;
    private readonly string? _publicKeyPem = publicKeyPem;
    private readonly string _userAgent = userAgent ?? "Shiyu";

    /// <summary>
    /// GitHub refuses anonymous API calls without a User-Agent. On the shared
    /// process client the header cannot live on the client itself — it would
    /// claim every request Shiyu makes is an update check — so it rides on
    /// each message.
    /// </summary>
    private async Task<HttpResponseMessage> GetAsync(
        string url, HttpCompletionOption completion, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(_userAgent);
        return await _http.SendAsync(request, completion, cancel);
    }

    /// <summary>The latest release, or null when the channel said nothing usable.</summary>
    public async Task<ReleaseManifest?> CheckAsync(CancellationToken cancel = default)
    {
        var url = _channel.LatestUrl;
#if DEBUG
        // Debug only: a release build takes its update source from nothing
        // but the compiled-in channel.
        if (Environment.GetEnvironmentVariable(ApiOverrideVariable) is { Length: > 0 } overrideBase)
        {
            url = overrideBase.TrimEnd('/') + "/releases/latest";
        }
#endif

        using var response = await GetAsync(url, HttpCompletionOption.ResponseContentRead, cancel);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return ReleaseManifest.Parse(await response.Content.ReadAsStringAsync(cancel));
    }

    /// <summary>
    /// Downloads, verifies and stages an update. Every refusal clears the
    /// whole staging area — the ticket's "no half installers" is a directory
    /// invariant, not a promise.
    /// </summary>
    public async Task<(bool Ok, string Error)> DownloadAsync(
        ReleaseManifest release, IProgress<double>? progress, CancellationToken cancel)
    {
        try
        {
            if (release.Asset(_channel.AssetName) is not { } asset)
            {
                UpdateStaging.Reset(_dataDirectory);
                return (false, $"这个发布（v{release.Version.Text}）没有 {_channel.AssetName}。");
            }

            if (release.SignatureFor(asset.Name) is not { } signatureAsset)
            {
                // 没有签名就没有信任可言——这不是"少了一个可选附件"。
                UpdateStaging.Reset(_dataDirectory);
                return (false, $"这个发布（v{release.Version.Text}）没有安装包签名（{asset.Name}.sig），已拒绝安装。");
            }

            // 拒绝即空目录：从第一步起就没有旧失败留下的东西。
            UpdateStaging.Reset(_dataDirectory);
            Directory.CreateDirectory(UpdateStaging.Root(_dataDirectory));

            var partialPath = UpdateStaging.InstallerPath(_dataDirectory) + ".partial";
            await DownloadFileAsync(asset.Url, partialPath, asset.Size, progress, cancel);
            File.Move(partialPath, UpdateStaging.InstallerPath(_dataDirectory), overwrite: true);

            // 信任之门开在一切内容检查之前：验不过，大小、校验和、zip
            // 结构一概不看了。
            var signature = await _http.GetByteArrayAsync(signatureAsset.Url, cancel);
            var installer = await File.ReadAllBytesAsync(UpdateStaging.InstallerPath(_dataDirectory), cancel);
            if (!UpdateSignature.Verify(installer, signature, _publicKeyPem))
            {
                UpdateStaging.Reset(_dataDirectory);
                return (false, "更新包签名不符，已拒绝安装。");
            }

            // 完整性（不是信任）：带了 .sha256 就必须下得来、对得上。
            var checksum = await DownloadChecksumAsync(release, asset, cancel);
            var (ok, error) = UpdateStaging.VerifyInstaller(
                UpdateStaging.InstallerPath(_dataDirectory), asset.Size, checksum);
            if (!ok)
            {
                UpdateStaging.Reset(_dataDirectory);
                return (false, error);
            }

            UpdateStaging.Extract(
                UpdateStaging.InstallerPath(_dataDirectory), UpdateStaging.StagedDirectory(_dataDirectory));
            UpdateStaging.WritePending(_dataDirectory, release);
            return (true, string.Empty);
        }
        catch (OperationCanceledException)
        {
            UpdateStaging.Reset(_dataDirectory);
            return (false, "已取消。");
        }
        catch (HttpRequestException failure)
        {
            UpdateStaging.Reset(_dataDirectory);
            return (false, "下载失败：" + failure.Message);
        }
        catch (IOException failure)
        {
            UpdateStaging.Reset(_dataDirectory);
            return (false, "写入失败：" + failure.Message);
        }
    }

    private async Task<string?> DownloadChecksumAsync(
        ReleaseManifest release, ReleaseAsset asset, CancellationToken cancel)
    {
        if (release.ChecksumFor(asset.Name) is not { } checksumAsset)
        {
            // 发布真的没带校验和：完整性由大小与验签兜底，这不是旁路。
            return null;
        }

        // 带了却下不来 → 请求抛 HttpRequestException，由调用方拒绝并清空。
        // 从前的"下不来就当作没有"正是被删掉的 fail-open。
        using var checksum = await GetAsync(
            checksumAsset.Url, HttpCompletionOption.ResponseContentRead, cancel);
        checksum.EnsureSuccessStatusCode();
        return await checksum.Content.ReadAsStringAsync(cancel);
    }

    private async Task DownloadFileAsync(
        string url, string path, long expectedSize, IProgress<double>? progress, CancellationToken cancel)
    {
        using var response = await GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancel);
        await using var target = File.Create(path);

        var copied = 0L;
        var buffer = new byte[64 * 1024];
        int read;

        while ((read = await source.ReadAsync(buffer, cancel)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancel);
            copied += read;

            if (expectedSize > 0)
            {
                progress?.Report((double)copied / expectedSize);
            }
        }
    }
}
