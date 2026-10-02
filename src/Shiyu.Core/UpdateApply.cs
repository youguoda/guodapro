namespace Shiyu.Core;

/// <summary>How the staged swap ended.</summary>
public enum ApplyOutcome
{
    /// <summary>The new version is in place and the old one is gone.</summary>
    Applied,

    /// <summary>Something refused mid-swap; the original was restored and still boots.</summary>
    RolledBack,

    /// <summary>Nothing to apply.</summary>
    NothingStaged,
}

/// <summary>
/// The final act of updating (ticket 28): the staged copy of the new version
/// replaces the installed one, in a way that cannot strand the user without a
/// bootable application.
///
/// It runs inside the NEW executable, launched from the staging directory
/// while the old one is still running — a process cannot replace its own
/// files. The sequence: wait the old process out, back the install directory
/// up, copy the staged files over, and on ANY failure restore the backup. The
/// data directory is never touched: history, images and settings survive by
/// construction, and any database schema change rides the existing
/// add-only migration path when the new version first opens the store.
/// </summary>
public static class UpdateApply
{
    /// <param name="stagedDirectory">The extracted new version.</param>
    /// <param name="installDirectory">Where the running application lives.</param>
    /// <param name="backupDirectory">Scratch space for the rollback copy; cleaned on success.</param>
    /// <param name="oldProcessId">The process being replaced, if it is still running.</param>
    /// <param name="waitForOldProcess">Injection point: blocks until the old process is gone or timed out.</param>
    public static ApplyOutcome Finalize(
        string stagedDirectory,
        string installDirectory,
        string backupDirectory,
        int? oldProcessId,
        Func<int, bool> waitForOldProcess)
    {
        if (!Directory.Exists(stagedDirectory)
            || !Directory.EnumerateFiles(stagedDirectory, "*", SearchOption.AllDirectories).Any())
        {
            return ApplyOutcome.NothingStaged;
        }

        if (oldProcessId is { } pid && !waitForOldProcess(pid))
        {
            // The old version never exited; it is still the bootable one.
            return ApplyOutcome.RolledBack;
        }

        if (Directory.Exists(backupDirectory))
        {
            Directory.Delete(backupDirectory, recursive: true);
        }
        Directory.CreateDirectory(backupDirectory);

        try
        {
            // The rollback copy: everything installed, into the backup.
            foreach (var entry in Directory.EnumerateFiles(installDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(installDirectory, entry);
                var target = Path.Combine(backupDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(entry, target, overwrite: true);
            }

            // The swap: staged files over the install. New files arrive,
            // changed files replace; nothing installed is deleted, so an
            // unknown file a later version stopped shipping is clutter, not
            // a casualty.
            foreach (var entry in Directory.EnumerateFiles(stagedDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(stagedDirectory, entry);
                var target = Path.Combine(installDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(entry, target, overwrite: true);
            }
        }
        catch (Exception failure)
        {
            // 换装失败走回滚，调用方（finalizer）自有 trace；但 O-24 要求
            // 任何路径都留一行——本进程通常还没挂 sink，这里是自记录的
            // 保险，挂了 sink 的宿主照收。
            Log.Event(LogEvent.UpdateApplyFailed, failure, ("stage", 2));
            Restore(backupDirectory, installDirectory);
            return ApplyOutcome.RolledBack;
        }

        Directory.Delete(backupDirectory, recursive: true);
        return ApplyOutcome.Applied;
    }

    /// <summary>
    /// Puts the backup back, file by file, best effort. Called only from the
    /// failure path — and the failure path must not throw.
    /// </summary>
    private static void Restore(string backupDirectory, string installDirectory)
    {
        try
        {
            foreach (var entry in Directory.EnumerateFiles(backupDirectory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(backupDirectory, entry);
                var target = Path.Combine(installDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(entry, target, overwrite: true);
            }
        }
        catch (Exception failure) when (
            failure is IOException or UnauthorizedAccessException)
        {
            // expected: 回滚自己失败意味着杀软在换装途中隔离了文件或更糟；
            // 备份目录被特意留下，缺什么可手工找回。失败路径不得再抛。
        }
    }
}
