namespace Shiyu.Core;

public sealed partial class EntryStore
{
    /// <summary>One entry by id, or null — the batch translator's lookup.</summary>
    public Entry? Get(long id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT id, text, source_app, created_at, kind, thumbnail, original_path, pinned, sub_type, html, rtf, files, favorite, note, use_count, group_id, translated_from, image_width, image_height,
                   (SELECT group_concat(t.name, char(31)) FROM tags t
                      JOIN entry_tags et ON et.tag_id = t.id WHERE et.entry_id = entries.id)
            FROM entries
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        return ReadEntries(command).FirstOrDefault();
    }

    /// <summary>
    /// Files a translation as a first-class entry, linked to what it came
    /// from. It is the user's kept work, not a copy they made — the list says
    /// so through the link, and it deletes like anything else.
    /// </summary>
    public Entry AppendTranslation(string text, string? sourceApp, DateTimeOffset createdAt, long? translatedFrom)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO entries (text, source_app, created_at, kind, translated_from)
            VALUES ($text, $sourceApp, $createdAt, 0, $from)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$sourceApp", (object?)sourceApp ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", createdAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$from", (object?)translatedFrom ?? DBNull.Value);

        var id = (long)command.ExecuteScalar()!;
        return new Entry(id, text, sourceApp, createdAt) { TranslatedFrom = translatedFrom };
    }
}

public sealed partial class ClipboardPipeline
{
    /// <summary>
    /// Keeps a translation the user asked to keep. It passes the same
    /// exclusion gate as any copy — being our own output earns no pass: a
    /// translation of something excluded by content rule stays out too.
    /// </summary>
    public bool RecordTranslation(string translated, long? translatedFrom, string? sourceApp = "Shiyu")
    {
        if (_exclusions.Excludes(new ClipboardSnapshot(translated, sourceApp, false)))
        {
            return false;
        }

        _store.AppendTranslation(translated, sourceApp, _clock.GetUtcNow(), translatedFrom);
        return true;
    }
}

public sealed record TranslationBatchResult(int Translated, int Skipped, int Failed);

/// <summary>
/// Translates a chosen set of entries and files each result as a linked
/// translation entry.
///
/// The standing rule of the model features holds here too: only what the user
/// selected is sent, one entry per request, nothing neighbouring — and every
/// request is kept in <see cref="SentRequests"/> precisely so a test can
/// prove it.
/// </summary>
public sealed class TranslationBatch(
    EntryStore store,
    ClipboardPipeline pipeline,
    ExclusionPolicy exclusions,
    IStreamingModel model,
    string targetLanguage,
    string? sourceLanguage = null)
{
    private readonly List<ModelRequest> _sent = [];

    public IReadOnlyList<ModelRequest> SentRequests => _sent;

    public static string PromptFor(string target, string? source) => $"""
        Translate the user's text into {target}{(source is { Length: > 0 } ? $" from {source}" : "")}.
        Output only the translation — no preamble, no notes, no quoting the original.
        """;

    public async Task<TranslationBatchResult> RunAsync(
        IReadOnlyList<long> entryIds,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancellation = default)
    {
        var translated = 0;
        var skipped = 0;
        var failed = 0;
        var done = 0;

        foreach (var id in entryIds)
        {
            if (cancellation.IsCancellationRequested)
            {
                break;
            }

            var entry = store.Get(id);
            done++;

            // Null (deleted mid-run), already a translation (translating a
            // translation chains noise), or excluded: none of these are sent.
            if (entry is null
                || entry.TranslatedFrom is not null
                || entry.Kind != EntryKind.Text
                || exclusions.Excludes(new ClipboardSnapshot(entry.Text, entry.SourceApp, false)))
            {
                skipped++;
                progress?.Report((done, entryIds.Count));
                continue;
            }

            var request = new ModelRequest(PromptFor(targetLanguage, sourceLanguage), entry.Text);
            _sent.Add(request);

            var builder = new System.Text.StringBuilder();
            try
            {
                await foreach (var piece in model.StreamAsync(request, cancellation))
                {
                    builder.Append(piece);
                }

                var result = builder.ToString().Trim();
                if (result.Length == 0)
                {
                    failed++;
                }
                else if (pipeline.RecordTranslation(result, entry.Id))
                {
                    translated++;
                }
                else
                {
                    // The exclusion gate refused the translation itself.
                    skipped++;
                }
            }
            catch (OperationCanceledException)
            {
                progress?.Report((done, entryIds.Count));
                throw;
            }
            catch (Exception)
            {
                // One unreachable entry does not take the batch down; the
                // rest keep going and the summary says how many fell.
                failed++;
            }

            progress?.Report((done, entryIds.Count));
        }

        return new TranslationBatchResult(translated, skipped, failed);
    }
}
