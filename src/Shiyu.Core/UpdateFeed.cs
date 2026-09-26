using System.Text.Json;

namespace Shiyu.Core;

/// <summary>
/// A release version, parsed the way tags actually look: "v1.2.3", "1.2.3",
/// "1.2", "1". Comparison is field by field — a version is not a string.
/// </summary>
public readonly record struct UpdateVersion(int Major, int Minor, int Patch)
    : IComparable<UpdateVersion>
{
    public static UpdateVersion? Parse(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var trimmed = text.Trim().TrimStart('v', 'V');

        var parts = trimmed.Split('.');
        if (parts.Length is < 1 or > 3 || parts.Any(part => part.Length == 0 || !part.All(char.IsDigit)))
        {
            return null;
        }

        var numbers = parts.Select(int.Parse).ToList();
        while (numbers.Count < 3)
        {
            numbers.Add(0);
        }

        return new UpdateVersion(numbers[0], numbers[1], numbers[2]);
    }

    public string Text => $"{Major}.{Minor}.{Patch}";

    public int CompareTo(UpdateVersion other)
    {
        var byField = Major.CompareTo(other.Major);
        if (byField != 0)
        {
            return byField;
        }

        byField = Minor.CompareTo(other.Minor);
        return byField != 0 ? byField : Patch.CompareTo(other.Patch);
    }
}

/// <summary>One downloadable file hanging off a release.</summary>
public sealed record ReleaseAsset(string Name, long Size, string Url);

/// <summary>
/// A parsed release from the GitHub Releases API, shaped for the updater: the
/// version tag, the notes, and the assets with their exact sizes.
/// </summary>
public sealed record ReleaseManifest(
    UpdateVersion Version,
    string Notes,
    DateTimeOffset Published,
    IReadOnlyList<ReleaseAsset> Assets)
{
    public static ReleaseManifest? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (UpdateVersion.Parse(root.GetProperty("tag_name").GetString()) is not { } version)
            {
                return null;
            }

            var notes = root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String
                ? body.GetString() ?? string.Empty
                : string.Empty;

            var published = root.TryGetProperty("published_at", out var publishedAt)
                && DateTimeOffset.TryParse(publishedAt.GetString(), out var moment)
                    ? moment
                    : DateTimeOffset.MinValue;

            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in list.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var name)
                        && asset.TryGetProperty("size", out var size)
                        && asset.TryGetProperty("browser_download_url", out var url))
                    {
                        assets.Add(new ReleaseAsset(
                            name.GetString() ?? string.Empty,
                            size.GetInt64(),
                            url.GetString() ?? string.Empty));
                    }
                }
            }

            return new ReleaseManifest(version, notes, published, assets);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// The installer asset for this machine, by name; null when the release
    /// shipped without one (a source-only tag, say) — that is "nothing to
    /// install", not an error.
    /// </summary>
    public ReleaseAsset? Asset(string name) => Assets.FirstOrDefault(asset => asset.Name == name);

    /// <summary>The checksum asset beside the installer, when the release ships one.</summary>
    public ReleaseAsset? ChecksumFor(string assetName)
        => Assets.FirstOrDefault(asset => asset.Name == assetName + ".sha256");

    public bool IsNewerThan(UpdateVersion current) => Version.CompareTo(current) > 0;
}

/// <summary>Where releases come from: one GitHub repository, one asset name.</summary>
public sealed record UpdateChannel(string Owner, string Repo, string AssetName)
{
    /// <summary>The shipping channel. One constant; changing it is a release decision.</summary>
    public static UpdateChannel Default { get; } = new("guodapro", "shiyu", "shiyu-win-x64.zip");

    public string LatestUrl => $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
}
