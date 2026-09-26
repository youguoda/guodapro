using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class UpdateFeedTests
{
    [Fact]
    public void Tags_parse_with_or_without_the_v()
    {
        Assert.Equal(new UpdateVersion(1, 2, 3), UpdateVersion.Parse("v1.2.3"));
        Assert.Equal(new UpdateVersion(1, 2, 3), UpdateVersion.Parse("1.2.3"));
        Assert.Equal(new UpdateVersion(1, 0, 0), UpdateVersion.Parse("1"));
        Assert.Equal(new UpdateVersion(1, 2, 0), UpdateVersion.Parse("1.2"));
    }

    [Fact]
    public void Anything_that_is_not_a_version_is_refused_not_guessed()
    {
        Assert.Null(UpdateVersion.Parse(null));
        Assert.Null(UpdateVersion.Parse("latest"));
        Assert.Null(UpdateVersion.Parse("1.2.x"));
        Assert.Null(UpdateVersion.Parse(""));
    }

    [Fact]
    public void Comparison_is_field_by_field_not_stringwise()
    {
        // String comparison would call 0.10.0 older than 0.9.0.
        Assert.True(UpdateVersion.Parse("0.10.0")!.Value.CompareTo(UpdateVersion.Parse("0.9.0")!.Value) > 0);
        Assert.True(UpdateVersion.Parse("2.0.0")!.Value.CompareTo(UpdateVersion.Parse("1.99.99")!.Value) > 0);
        Assert.Equal(UpdateVersion.Parse("1.2.3"), UpdateVersion.Parse("v1.2.3"));
    }

    private const string ReleaseJson = """
        {
          "tag_name": "v1.4.2",
          "body": "修复了图片记录的一个问题。",
          "published_at": "2026-09-20T08:00:00Z",
          "assets": [
            { "name": "shiyu-win-x64.zip", "size": 71234567, "browser_download_url": "https://example.invalid/shiyu-win-x64.zip" },
            { "name": "shiyu-win-x64.zip.sha256", "size": 88, "browser_download_url": "https://example.invalid/shiyu-win-x64.zip.sha256" },
            { "name": "sources.zip", "size": 100, "browser_download_url": "https://example.invalid/sources.zip" }
          ]
        }
        """;

    [Fact]
    public void A_release_parses_into_version_notes_and_assets()
    {
        var release = ReleaseManifest.Parse(ReleaseJson);

        Assert.NotNull(release);
        Assert.Equal(new UpdateVersion(1, 4, 2), release.Version);
        Assert.Contains("修复", release.Notes);
        Assert.Equal(3, release.Assets.Count);

        var installer = release.Asset("shiyu-win-x64.zip");
        Assert.NotNull(installer);
        Assert.Equal(71234567, installer.Size);

        var checksum = release.ChecksumFor("shiyu-win-x64.zip");
        Assert.NotNull(checksum);
        Assert.Equal("shiyu-win-x64.zip.sha256", checksum.Name);
    }

    [Fact]
    public void A_release_without_a_tag_is_not_a_release()
    {
        Assert.Null(ReleaseManifest.Parse("{}"));
        Assert.Null(ReleaseManifest.Parse("not json at all"));
    }

    [Fact]
    public void Newer_is_decided_by_the_version_not_the_date()
    {
        var release = ReleaseManifest.Parse(ReleaseJson)!;

        Assert.True(release.IsNewerThan(new UpdateVersion(1, 4, 1)));
        Assert.False(release.IsNewerThan(new UpdateVersion(1, 4, 2)));
        Assert.False(release.IsNewerThan(new UpdateVersion(9, 0, 0)));
    }
}
