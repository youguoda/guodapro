using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class UpdateApplyTests
{
    private static void Write(string root, string relative, string content)
    {
        var path = System.IO.Path.Combine(root, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Read(string root, string relative)
        => File.ReadAllText(System.IO.Path.Combine(root, relative));

    [Fact]
    public void The_swap_replaces_the_install_and_cleans_up()
    {
        using var area = new TempDirectory();
        var staged = System.IO.Path.Combine(area.Path, "staged");
        var install = System.IO.Path.Combine(area.Path, "install");
        var backup = System.IO.Path.Combine(area.Path, "backup");
        Directory.CreateDirectory(install);
        Write(install, "Shiyu.App.exe", "old");
        Write(install, "Shiyu.Core.dll", "old core");

        Write(staged, "Shiyu.App.exe", "NEW");
        Write(staged, "Shiyu.Core.dll", "NEW core");
        Write(staged, "Shiyu.Windows.dll", "new file");

        var outcome = UpdateApply.Finalize(staged, install, backup, oldProcessId: null, waitForOldProcess: _ => true);

        Assert.Equal(ApplyOutcome.Applied, outcome);
        Assert.Equal("NEW", Read(install, "Shiyu.App.exe"));
        Assert.Equal("NEW core", Read(install, "Shiyu.Core.dll"));
        Assert.Equal("new file", Read(install, "Shiyu.Windows.dll"));
        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public void An_empty_stage_is_nothing_to_apply()
    {
        using var area = new TempDirectory();
        var install = System.IO.Path.Combine(area.Path, "install");
        Directory.CreateDirectory(install);

        var outcome = UpdateApply.Finalize(
            System.IO.Path.Combine(area.Path, "staged"), install,
            System.IO.Path.Combine(area.Path, "backup"), null, _ => true);

        Assert.Equal(ApplyOutcome.NothingStaged, outcome);
    }

    [Fact]
    public void A_refusing_target_rolls_the_install_back_to_bootable()
    {
        using var area = new TempDirectory();
        var staged = System.IO.Path.Combine(area.Path, "staged");
        var install = System.IO.Path.Combine(area.Path, "install");
        var backup = System.IO.Path.Combine(area.Path, "backup");
        Directory.CreateDirectory(install);
        Write(install, "Shiyu.App.exe", "old bootable");

        Write(staged, "Shiyu.App.exe", "new");

        // The refusal: a target file that cannot be overwritten — read-only
        // does it on Windows.
        var locked = System.IO.Path.Combine(install, "Shiyu.App.exe");
        var attributes = File.GetAttributes(locked);

        try
        {
            File.SetAttributes(locked, attributes | FileAttributes.ReadOnly);

            var outcome = UpdateApply.Finalize(staged, install, backup, null, _ => true);

            Assert.Equal(ApplyOutcome.RolledBack, outcome);
            Assert.Equal("old bootable", File.ReadAllText(locked));
        }
        finally
        {
            File.SetAttributes(locked, attributes);
        }
    }

    [Fact]
    public void An_old_process_that_never_exits_aborts_without_touching_anything()
    {
        using var area = new TempDirectory();
        var staged = System.IO.Path.Combine(area.Path, "staged");
        var install = System.IO.Path.Combine(area.Path, "install");
        Directory.CreateDirectory(install);
        Write(install, "Shiyu.App.exe", "old");
        Write(staged, "Shiyu.App.exe", "new");

        var outcome = UpdateApply.Finalize(
            staged, install, System.IO.Path.Combine(area.Path, "backup"),
            oldProcessId: 42, waitForOldProcess: _ => false);

        Assert.Equal(ApplyOutcome.RolledBack, outcome);
        Assert.Equal("old", Read(install, "Shiyu.App.exe"));
    }
}
