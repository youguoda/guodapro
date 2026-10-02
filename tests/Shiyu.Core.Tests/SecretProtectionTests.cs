using System.Text.Json;

namespace Shiyu.Core.Tests;

/// <summary>
/// A protector whose "protection" is a marker plus base64 — enough to test
/// the settings round trip without any Windows dependency.
/// </summary>
internal sealed class FakeProtector : ISecretProtector
{
    public bool FailUnprotect { get; set; }

    public string? Protect(string plain)
        => AppSettings.SecretMarker + Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(plain));

    public string? Unprotect(string stored)
    {
        if (FailUnprotect)
        {
            return null;
        }

        var payload = stored.StartsWith(AppSettings.SecretMarker, StringComparison.Ordinal)
            ? stored[AppSettings.SecretMarker.Length..]
            : stored;
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
    }
}

[Collection("settings-io")]
public class SecretProtectionTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "shiyu-secret-tests-" + Guid.NewGuid().ToString("N") + ".json");

    public SecretProtectionTests()
    {
        AppSettings.SecretProtector = new FakeProtector();
    }

    public void Dispose()
    {
        AppSettings.SecretProtector = null;
        try { File.Delete(_path); } catch (IOException) { }
    }

    [Fact]
    public void A_saved_key_is_protected_on_disk_and_plain_in_memory()
    {
        var settings = new AppSettings { BackendApiKey = "sk-live-123" };

        settings.Save(_path);

        var stored = File.ReadAllText(_path);
        Assert.DoesNotContain("sk-live-123", stored);
        Assert.Contains(AppSettings.SecretMarker, stored);
        Assert.Equal("sk-live-123", settings.BackendApiKey);

        Assert.True(AppSettings.TryParse(stored, out var loaded));
        Assert.Equal("sk-live-123", loaded!.BackendApiKey);
    }

    [Fact]
    public void Plaintext_from_an_older_shiyu_loads_as_is_and_is_protected_on_next_save()
    {
        var legacy = JsonSerializer.Serialize(
            new AppSettings { BackendApiKey = "sk-legacy" },
            new JsonSerializerOptions { WriteIndented = true });

        Assert.True(AppSettings.TryParse(legacy, out var loaded));
        Assert.Equal("sk-legacy", loaded!.BackendApiKey);

        loaded.Save(_path);
        Assert.DoesNotContain("sk-legacy", File.ReadAllText(_path));
    }

    [Fact]
    public void A_key_this_machine_cannot_open_loads_empty_not_as_garbage()
    {
        var protector = (FakeProtector)AppSettings.SecretProtector!;
        protector.FailUnprotect = true;

        var stored = JsonSerializer.Serialize(
            new AppSettings { BackendApiKey = "sk-gone" },
            new JsonSerializerOptions { WriteIndented = true })
            .Replace("sk-gone", protector.Protect("sk-gone"));

        Assert.True(AppSettings.TryParse(stored, out var loaded));
        Assert.Equal(string.Empty, loaded!.BackendApiKey);
    }

    [Fact]
    public void Without_a_protector_a_stored_key_is_dropped_rather_than_sent_as_a_credential()
    {
        var protectedJson = JsonSerializer.Serialize(
            new AppSettings { BackendApiKey = new FakeProtector().Protect("sk-any")! },
            new JsonSerializerOptions { WriteIndented = true });

        AppSettings.SecretProtector = null;

        Assert.True(AppSettings.TryParse(protectedJson, out var loaded));
        Assert.Equal(string.Empty, loaded!.BackendApiKey);

        // And saving stays plaintext — a tool reading settings.json outside
        // the app must not find marker soup it cannot reproduce.
        var settings = new AppSettings { BackendApiKey = "sk-plain" };
        settings.Save(_path);
        Assert.Contains("sk-plain", File.ReadAllText(_path));
    }

    [Fact]
    public void The_backup_copy_omits_the_key_by_default_and_carries_it_only_on_request()
    {
        var settings = new AppSettings { BackendApiKey = "sk-backup" };

        Assert.DoesNotContain("sk-backup", settings.ToBackupJson(includeKey: false));
        Assert.Contains("sk-backup", settings.ToBackupJson(includeKey: true));
    }
}
