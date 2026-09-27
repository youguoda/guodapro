using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class AppSettingsTests
{
    private sealed class TempFile : IDisposable
    {
        private readonly string _directory;

        public TempFile()
        {
            _directory = Path.Combine(Path.GetTempPath(), "shiyu-settings", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            Path_ = Path.Combine(_directory, "settings.json");
        }

        public string Path_ { get; }

        public void Dispose()
        {
            try { Directory.Delete(_directory, recursive: true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public void A_missing_file_gives_usable_defaults()
    {
        using var file = new TempFile();

        var settings = AppSettings.Load(file.Path_);

        Assert.Equal("Chinese", settings.TargetLanguage);
        Assert.True(settings.StartWithWindows);
        Assert.Equal(30, settings.ImageRetentionDays);
        Assert.False(settings.Backend.IsConfigured);

        // The narrow bar: a hotkey of its own, density knobs in the middle of
        // their range, and no remembered position yet.
        Assert.Equal("Ctrl+Shift+B", settings.BarHotkey);
        Assert.Equal(4, settings.BarTextLines);
        Assert.Equal(120, settings.BarImageHeight);
        Assert.Equal(3, settings.BarFileCount);
        Assert.Null(settings.BarLeft);

        // Pinning to the top of the z-order is the resident bar's working
        // posture; turning it off is a deliberate act (ticket 39).
        Assert.True(settings.BarAlwaysOnTop);
    }

    [Fact]
    public void Settings_survive_a_round_trip()
    {
        using var file = new TempFile();
        var original = new AppSettings
        {
            TargetLanguage = "English",
            SourceLanguage = "Chinese",
            BackendBaseUrl = "https://example.com/v1",
            BackendModel = "some-model",
            BackendApiKey = "a-secret",
            ImageRetentionDays = 7,
            StartWithWindows = false,
            Theme = AppTheme.Dark,
            BarHotkey = "Ctrl+Alt+B",
            BarTextLines = 6,
            BarImageHeight = 200,
            BarFileCount = 5,
            BarAlwaysOnTop = false,
            BarLeft = 12.5,
            BarTop = 34.5,
            BarHeight = 800,
            ExclusionRules = [new StoredExclusionRule(ExclusionRuleKind.SourceApp, "MyVault")],
        };

        original.Save(file.Path_);
        var loaded = AppSettings.Load(file.Path_);

        // Compared field by field rather than as whole records: the rule list
        // is an interface reference, which records compare by identity.
        Assert.Equal(original.TargetLanguage, loaded.TargetLanguage);
        Assert.Equal(original.SourceLanguage, loaded.SourceLanguage);
        Assert.Equal(original.BackendBaseUrl, loaded.BackendBaseUrl);
        Assert.Equal(original.BackendModel, loaded.BackendModel);
        Assert.Equal(original.BackendApiKey, loaded.BackendApiKey);
        Assert.Equal(original.ImageRetentionDays, loaded.ImageRetentionDays);
        Assert.Equal(original.StartWithWindows, loaded.StartWithWindows);
        Assert.Equal(AppTheme.Dark, loaded.Theme);
        Assert.Equal("Ctrl+Alt+B", loaded.BarHotkey);
        Assert.Equal(6, loaded.BarTextLines);
        Assert.Equal(200, loaded.BarImageHeight);
        Assert.Equal(5, loaded.BarFileCount);
        Assert.False(loaded.BarAlwaysOnTop);
        Assert.Equal(12.5, loaded.BarLeft);
        Assert.Equal(34.5, loaded.BarTop);
        Assert.Equal(800, loaded.BarHeight);
        Assert.Equal(original.ExclusionRules, loaded.ExclusionRules);
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_defaults_instead_of_stopping_startup()
    {
        using var file = new TempFile();
        File.WriteAllText(file.Path_, "{ this is not json");

        var settings = AppSettings.Load(file.Path_);

        // Shiyu has no window to show an error in; refusing to start would look
        // to the user like a tool that simply died.
        Assert.Equal("Chinese", settings.TargetLanguage);
    }

    [Fact]
    public void An_interrupted_save_leaves_the_previous_settings_intact()
    {
        using var file = new TempFile();
        new AppSettings { TargetLanguage = "English" }.Save(file.Path_);

        // A stale temporary file from a save that never finished must not be
        // mistaken for the real thing.
        File.WriteAllText(file.Path_ + ".tmp", "{ half written");

        Assert.Equal("English", AppSettings.Load(file.Path_).TargetLanguage);
    }

    [Fact]
    public void The_users_own_rules_are_added_to_the_shipped_presets_rather_than_replacing_them()
    {
        var settings = new AppSettings
        {
            ExclusionRules = [new StoredExclusionRule(ExclusionRuleKind.SourceApp, "MyVault")],
        };

        var policy = settings.BuildExclusionPolicy();

        // Losing the presets because the user added one rule of their own would
        // quietly reopen the hole they are there to close.
        Assert.Contains(policy.Rules, rule => rule.Value == "MyVault");
        Assert.Contains(policy.Rules, rule => rule.Value == "KeePassXC");
    }

    [Fact]
    public void Saving_creates_the_directory_when_it_is_not_there_yet()
    {
        var directory = Path.Combine(Path.GetTempPath(), "shiyu-settings", Guid.NewGuid().ToString("N"), "nested");
        var path = Path.Combine(directory, "settings.json");

        try
        {
            new AppSettings().Save(path);
            Assert.True(File.Exists(path));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true); }
            catch (IOException) { }
        }
    }
}
