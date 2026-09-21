namespace Shiyu.Core.Tests;

/// <summary>
/// A real SQLite database file in a temp directory, deleted on dispose.
/// Storage is deliberately never faked: full-text search, filtering and
/// retention are the kind of logic a fake would silently stop testing.
/// </summary>
public sealed class TempDatabase : IDisposable
{
    private readonly string _directory;

    public TempDatabase()
    {
        _directory = Path.Combine(Path.GetTempPath(), "shiyu-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        FilePath = Path.Combine(_directory, "history.db");
    }

    public string FilePath { get; }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { /* a stray handle must not fail the test */ }
    }
}
