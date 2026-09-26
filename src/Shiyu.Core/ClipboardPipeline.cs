using System.Text;

namespace Shiyu.Core;

/// <summary>
/// Turns clipboard changes into entries. Recording is greedy: every copy is
/// kept, regardless of what any later filter decides about the badge. The one
/// exception is exclusion, which is not a filter at all — excluded content is
/// never written down in the first place.
/// </summary>
public sealed partial class ClipboardPipeline : IDisposable
{
    private readonly IClipboardMonitor _clipboard;
    private readonly EntryStore _store;
    private readonly TimeProvider _clock;
    private ExclusionPolicy _exclusions;
    private readonly ImageArchive? _images;
    private readonly SourceIconCache? _icons;

    /// <summary>
    /// Raised after an entry is recorded, when its content is the kind a user
    /// might want translated. Raised from here rather than from the window so
    /// the ordering that matters — excluded content never reaches the badge —
    /// is settled in one place and can be tested.
    /// </summary>
    public event Action<string>? BadgeDeserved;

    /// <summary>
    /// Raised when an image could not be saved. The user is told rather than
    /// left with a copy that silently never appeared.
    /// </summary>
    public event Action<string>? ImageFailed;

    /// <summary>Completes when any in-flight image work has finished.</summary>
    public Task Idle { get; private set; } = Task.CompletedTask;

    public ClipboardPipeline(
        IClipboardMonitor clipboard,
        EntryStore store,
        TimeProvider clock,
        ExclusionPolicy exclusions,
        ImageArchive? images = null,
        SourceIconCache? icons = null)
    {
        _clipboard = clipboard;
        _store = store;
        _clock = clock;
        _exclusions = exclusions;
        _images = images;
        _icons = icons;
        _clipboard.Changed += OnClipboardChanged;
    }

    /// <summary>
    /// Swaps in a new set of rules. Takes effect from the very next copy, so a
    /// user who adds a rule does not have to restart before it protects them.
    /// </summary>
    public void UseExclusions(ExclusionPolicy exclusions) => _exclusions = exclusions;

    /// <summary>
    /// Whether copied file lists are recorded. Off means a file copy leaves
    /// no entry — the choice is "not recorded", never "recorded and hidden".
    /// </summary>
    public bool RecordFiles { get; set; } = true;

    /// <summary>Whether copied images are recorded. Text is always recorded.</summary>
    public bool RecordImages { get; set; } = true;

    private void OnClipboardChanged(ClipboardSnapshot snapshot)
    {
        // Checked first, and before anything is written or even read back:
        // excluded content must leave no trace at all, not a trace that is
        // later filtered out of view.
        if (_exclusions.Excludes(snapshot))
        {
            return;
        }

        // The copy is now definitely going to be an entry, so this is the
        // moment to make sure its source application has a cached icon — the
        // executable is at hand and alive. Ensure swallows its own failures;
        // recording never depends on it.
        _icons?.Ensure(snapshot.SourceApp, snapshot.SourceExePath);

        // A file copy: one entry for any number of files, like text and image
        // before it. Identical repeats of the newest file list touch it rather
        // than duplicating it.
        if (snapshot.Files is { Count: > 0 } files)
        {
            if (!RecordFiles)
            {
                return;
            }

            var newest = _store.MostRecent();
            if (newest is { Kind: EntryKind.Files } && newest.Files.SequenceEqual(files))
            {
                _store.Touch(newest.Id, _clock.GetUtcNow());
                return;
            }

            _store.AppendFiles(files, snapshot.SourceApp, _clock.GetUtcNow());
            return;
        }

        if (snapshot.Image is { } image && _images is not null)
        {
            if (!RecordImages)
            {
                return;
            }

            // Chained rather than fired and forgotten, so two quick copies are
            // recorded in the order they happened rather than whichever
            // encodes faster.
            Idle = Idle.ContinueWith(
                _ => RecordImage(image, snapshot.SourceApp),
                TaskScheduler.Default).Unwrap();
            return;
        }

        RecordText(snapshot);
    }

    private void RecordText(ClipboardSnapshot snapshot)
    {
        var now = _clock.GetUtcNow();

        // A single user copy can raise more than one clipboard notification,
        // because applications publish several formats in turn. Collapsing a
        // repeat of the newest entry keeps those phantom duplicates out of the
        // history; a repeat that is not adjacent still earns its own entry, so
        // the chronology stays honest.
        var newest = _store.MostRecent();
        if (newest is not null && newest.Kind == EntryKind.Text && newest.Text == snapshot.Text)
        {
            _store.Touch(newest.Id, now);
            Offer(snapshot.Text);
            return;
        }

        _store.Append(
            snapshot.Text,
            snapshot.SourceApp,
            now,
            WithinLimit(snapshot.Html),
            WithinLimit(snapshot.Rtf));
        Offer(snapshot.Text);
    }

    private async Task RecordImage(IClipboardImage image, string? sourceApp)
    {
        try
        {
            var rendered = await image.RenderAsync();
            var now = _clock.GetUtcNow();
            var path = _images!.Save(rendered.FullPng, now);

            _store.AppendImage(
                $"图片 {rendered.Width}×{rendered.Height}",
                rendered.ThumbnailPng,
                path,
                sourceApp,
                now,
                rendered.Width,
                rendered.Height);
        }
        catch (Exception failure)
        {
            // A failed image must not take the application down, and must not
            // vanish without a word either — the user saw themselves copy it.
            ImageFailed?.Invoke(failure.Message);
        }
    }

    /// <summary>
    /// The ceiling on a stored formatted form. A whole-page copy can be
    /// megabytes; keeping it would bloat the database for a formatting nobody
    /// will paste back, so past that the plain text stands alone.
    /// </summary>
    private const int FormattedLimitBytes = 512 * 1024;

    private static string? WithinLimit(string? formatted)
        => formatted is { Length: > 0 } && Encoding.UTF8.GetByteCount(formatted) <= FormattedLimitBytes
            ? formatted
            : null;

    private void Offer(string text)
    {
        if (ContentClassifier.DeservesBadge(text))
        {
            BadgeDeserved?.Invoke(text);
        }
    }

    public void Dispose() => _clipboard.Changed -= OnClipboardChanged;
}
