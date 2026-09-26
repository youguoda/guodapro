using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// The update channel in motion (ticket 28): check GitHub Releases, download
/// and verify the installer into the staging area, then hand the swap to a
/// second process — a running application cannot replace its own files.
///
/// The design rule from the ticket is baked into the shape: nothing here ever
/// writes into the install directory. The data directory holds the staging
/// area; the install directory is touched only by the finalizer, which
/// backs it up first and restores on any refusal.
/// </summary>
internal sealed class UpdateService
{
    /// <summary>An alternate API base for probes and self-hosted mirrors: SHIYU_UPDATE_API.</summary>
    private const string ApiOverrideVariable = "SHIYU_UPDATE_API";

    private readonly HttpClient _http = new()
    {
        // GitHub refuses anonymous API calls without one.
        DefaultRequestHeaders =
        {
            UserAgent = { new System.Net.Http.Headers.ProductInfoHeaderValue("Shiyu", Current.Text) },
        },
    };

    private readonly string _dataDirectory;

    private readonly UpdateChannel _channel;

    public UpdateService(string dataDirectory, UpdateChannel? channel = null)
    {
        _dataDirectory = dataDirectory;
        _channel = channel ?? UpdateChannel.Default;
    }

    public static UpdateVersion Current
        => UpdateVersion.Parse(Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)) ?? new UpdateVersion();

    /// <summary>The staged swap has this many attempts to see the old process out.</summary>
    private static readonly TimeSpan OldProcessTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The latest release, or null when the channel said nothing usable.</summary>
    public async Task<ReleaseManifest?> CheckAsync()
    {
        var url = Environment.GetEnvironmentVariable(ApiOverrideVariable) is { Length: > 0 } overrideBase
            ? overrideBase.TrimEnd('/') + "/releases/latest"
            : _channel.LatestUrl;

        using var response = await _http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return ReleaseManifest.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Downloads the installer to staging with progress, then verifies it
    /// (size, checksum when published, archive content) and extracts it. A
    /// failure at any step clears the whole staging area — the ticket's "no
    /// half installers" is a directory invariant, not a promise.
    /// </summary>
    public async Task<(bool Ok, string Error)> DownloadAsync(
        ReleaseManifest release, IProgress<double>? progress, CancellationToken cancel)
    {
        if (release.Asset(_channel.AssetName) is not { } asset)
        {
            return (false, $"这个发布（v{release.Version.Text}）没有 {_channel.AssetName}。");
        }

        UpdateStaging.Reset(_dataDirectory);
        var root = UpdateStaging.Root(_dataDirectory);
        Directory.CreateDirectory(root);

        try
        {
            var partialPath = UpdateStaging.InstallerPath(_dataDirectory) + ".partial";
            await DownloadFileAsync(asset.Url, partialPath, asset.Size, progress, cancel);
            File.Move(partialPath, UpdateStaging.InstallerPath(_dataDirectory), overwrite: true);

            var checksum = await TryDownloadChecksumAsync(release, asset, cancel);
            var (ok, error) = UpdateStaging.VerifyInstaller(
                UpdateStaging.InstallerPath(_dataDirectory), asset.Size, checksum);

            if (!ok)
            {
                UpdateStaging.Reset(_dataDirectory);
                return (false, error);
            }

            UpdateStaging.Extract(UpdateStaging.InstallerPath(_dataDirectory), UpdateStaging.StagedDirectory(_dataDirectory));
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

    private async Task<string?> TryDownloadChecksumAsync(ReleaseManifest release, ReleaseAsset asset, CancellationToken cancel)
    {
        if (release.ChecksumFor(asset.Name) is not { } checksumAsset)
        {
            return null;
        }

        try
        {
            return await _http.GetStringAsync(checksumAsset.Url, cancel);
        }
        catch (Exception)
        {
            // A checksum that cannot be fetched is treated as absent: the size
            // and archive checks still guard the swap.
            return null;
        }
    }

    private async Task DownloadFileAsync(
        string url, string path, long expectedSize, IProgress<double>? progress, CancellationToken cancel)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
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

    /// <summary>
    /// Starts the staged copy with the finalizer command and exits this
    /// instance. The two-phase shape is what keeps the old version bootable:
    /// the swap only happens once this process is gone, and only after a
    /// backup exists.
    /// </summary>
    public void ApplyAndRestart()
    {
        var stagedExe = Path.Combine(UpdateStaging.StagedDirectory(_dataDirectory), "Shiyu.App.exe");
        Trace($"apply: staged exists={File.Exists(stagedExe)}");
        if (!File.Exists(stagedExe))
        {
            return;
        }

        // Trailing backslashes trimmed on purpose: BaseDirectory ends with
        // one, and a backslash directly before a closing quote escapes it in
        // Windows command-line parsing — the finalizer would then receive a
        // mangled argument list and fall through as a normal application.
        var installDirectory = AppContext.BaseDirectory.TrimEnd('\\');
        var backup = Path.Combine(UpdateStaging.Root(_dataDirectory), "backup").TrimEnd('\\');

        var arguments =
            $"--finalize-update {Environment.ProcessId} " +
            $"\"{installDirectory}\" \"{backup}\" \"{_dataDirectory.TrimEnd('\\')}\"";

        try
        {
            var finalizer = Process.Start(new ProcessStartInfo(stagedExe, arguments) { UseShellExecute = false });
            Trace($"apply: finalizer pid={(finalizer is null ? "null" : finalizer.Id.ToString())}");
        }
        catch (Exception failure)
        {
            Trace("apply: start failed " + failure.Message);
            return;
        }

        System.Windows.Application.Current.Shutdown();
    }

    private static void Trace(string line)
    {
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shiyu-apply.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// The finalizer entry, run by the staged copy before any of the
    /// application starts: swap the install, restart the real application,
    /// and never show a window. True when the arguments were a finalizer call
    /// and the process should now exit.
    ///
    /// Arguments: <c>--finalize-update &lt;old pid&gt; &lt;install dir&gt; &lt;backup dir&gt; &lt;data dir&gt;</c>
    /// </summary>
    public static bool TryRunFinalizer(string[] arguments)
    {
        if (arguments.Length != 5 || arguments[0] != "--finalize-update"
            || !int.TryParse(arguments[1], out var oldProcessId))
        {
            return false;
        }

        var installDirectory = arguments[2];
        var backup = arguments[3];
        var staged = AppContext.BaseDirectory;
        var dataRoot = arguments[4];

        var outcome = UpdateApply.Finalize(
            staged,
            installDirectory,
            backup,
            oldProcessId,
            pid => WaitForExit(pid, OldProcessTimeout));

        Trace($"finalize: outcome={outcome} staged={staged} install={installDirectory}");

        if (outcome == ApplyOutcome.Applied)
        {
            try
            {
                // Launched before the staging reset on purpose: the reset
                // removes this process's own files, and everything after it
                // must already be loaded.
                Process.Start(new ProcessStartInfo(
                    Path.Combine(installDirectory, "Shiyu.App.exe")) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // The new version is installed; if it cannot even start here,
                // the user still has it to start by hand.
            }

            try
            {
                // Everything except the staged directory this process runs
                // from — that one is the relaunched application's first chore.
                UpdateStaging.CleanAfterApply(dataRoot);
            }
            catch (Exception)
            {
                // Staging leftovers are clutter, never a boot problem.
            }
        }

        return true;
    }

    private static bool WaitForExit(int processId, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            // Already gone — that is the good case.
            return true;
        }
    }
}
