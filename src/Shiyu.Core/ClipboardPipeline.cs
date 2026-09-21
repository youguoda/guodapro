namespace Shiyu.Core;

/// <summary>
/// Turns clipboard changes into entries. Recording is greedy: every copy is
/// kept, regardless of what any later filter decides about the badge.
/// </summary>
public sealed class ClipboardPipeline : IDisposable
{
    private readonly IClipboardMonitor _clipboard;
    private readonly EntryStore _store;
    private readonly TimeProvider _clock;

    public ClipboardPipeline(IClipboardMonitor clipboard, EntryStore store, TimeProvider clock)
    {
        _clipboard = clipboard;
        _store = store;
        _clock = clock;
        _clipboard.Changed += OnClipboardChanged;
    }

    private void OnClipboardChanged(ClipboardSnapshot snapshot)
    {
        var now = _clock.GetUtcNow();

        // A single user copy can raise more than one clipboard notification,
        // because applications publish several formats in turn. Collapsing a
        // repeat of the newest entry keeps those phantom duplicates out of the
        // history; a repeat that is not adjacent still earns its own entry, so
        // the chronology stays honest.
        var newest = _store.MostRecent();
        if (newest is not null && newest.Text == snapshot.Text)
        {
            _store.Touch(newest.Id, now);
            return;
        }

        _store.Append(snapshot.Text, snapshot.SourceApp, now);
    }

    public void Dispose() => _clipboard.Changed -= OnClipboardChanged;
}
