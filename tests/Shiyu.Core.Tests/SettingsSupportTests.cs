using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class CloudSyncedPathsTests
{
    [Theory]
    [InlineData(@"C:\Users\someone\OneDrive\Shiyu", "OneDrive")]
    [InlineData(@"C:\Users\someone\OneDrive - Contoso\Shiyu", "OneDrive - Contoso")]
    [InlineData(@"C:\Users\someone\Dropbox\tools\Shiyu", "Dropbox")]
    [InlineData(@"D:\Google Drive\Shiyu", "Google Drive")]
    [InlineData(@"C:\Users\someone\坚果云\拾语", "坚果云")]
    [InlineData(@"E:\百度网盘\shiyu", "百度网盘")]
    public void A_synced_folder_is_recognised_and_named(string path, string expected)
        => Assert.Equal(expected, CloudSyncedPaths.DetectSyncFolder(path));

    [Theory]
    [InlineData(@"C:\Users\someone\AppData\Local\Shiyu")]
    [InlineData(@"D:\Projects\Shiyu")]
    [InlineData(@"C:\Users\someone\Documents\Shiyu")]
    public void An_ordinary_folder_is_not_flagged(string path)
        => Assert.Null(CloudSyncedPaths.DetectSyncFolder(path));

    [Fact]
    public void A_folder_that_merely_mentions_a_sync_service_is_not_flagged()
    {
        // Warning about this one would train the user to dismiss the warning,
        // which is worse than not warning at all.
        Assert.Null(CloudSyncedPaths.DetectSyncFolder(@"D:\笔记\OneDrive迁移笔记"));
        Assert.Null(CloudSyncedPaths.DetectSyncFolder(@"D:\DropboxAlternatives\notes"));
    }

    [Fact]
    public void Detection_ignores_case_and_slash_direction()
    {
        Assert.NotNull(CloudSyncedPaths.DetectSyncFolder(@"c:\users\someone\onedrive\shiyu"));
        Assert.NotNull(CloudSyncedPaths.DetectSyncFolder("C:/Users/someone/Dropbox/shiyu"));
    }

    [Fact]
    public void Nothing_in_gives_nothing_out()
    {
        Assert.Null(CloudSyncedPaths.DetectSyncFolder(""));
        Assert.Null(CloudSyncedPaths.DetectSyncFolder("   "));
    }
}

public class HotkeySpecTests
{
    [Theory]
    [InlineData("Ctrl+Shift+Z")]
    [InlineData("ctrl+shift+z")]
    [InlineData(" Ctrl + Shift + Z ")]
    [InlineData("Control+Shift+Z")]
    public void The_usual_ways_of_writing_one_all_parse(string text)
    {
        var parsed = HotkeySpec.Parse(text);

        Assert.NotNull(parsed);
        Assert.Equal(HotkeyModifier.Control | HotkeyModifier.Shift, parsed.Modifiers);
        Assert.Equal('Z', parsed.Key);
    }

    [Fact]
    public void Formatting_puts_the_parts_in_a_fixed_order()
    {
        // So the settings file does not churn just because the user typed the
        // modifiers in a different order.
        Assert.Equal("Ctrl+Shift+Alt+Z", HotkeySpec.Parse("alt+shift+ctrl+z")!.ToString());
    }

    [Fact]
    public void A_parsed_hotkey_round_trips()
    {
        var text = HotkeySpec.Parse("Win+Alt+Q")!.ToString();

        Assert.Equal(HotkeySpec.Parse("Win+Alt+Q"), HotkeySpec.Parse(text));
    }

    [Theory]
    [InlineData("Z")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+F13")]
    [InlineData("Ctrl+Shift+")]
    public void Anything_that_is_not_a_usable_hotkey_parses_to_null(string text)
        => Assert.Null(HotkeySpec.Parse(text));

    [Fact]
    public void A_bare_letter_is_refused_even_though_it_looks_like_a_key()
    {
        // Registering "Z" globally would take that key away from every
        // application on the machine.
        Assert.Null(HotkeySpec.Parse("Z"));
    }

    [Fact]
    public void Digits_are_allowed_as_the_key()
    {
        var parsed = HotkeySpec.Parse("Ctrl+Alt+1");

        Assert.NotNull(parsed);
        Assert.Equal('1', parsed.Key);
    }
}
