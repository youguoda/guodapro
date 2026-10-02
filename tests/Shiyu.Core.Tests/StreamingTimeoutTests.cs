using System.Net;

namespace Shiyu.Core.Tests;

/// <summary>
/// Streaming timeouts split into first-byte and idle-between-chunks (O-23):
/// a translation may run as long as it keeps making progress — the old
/// whole-stream cap cut long outputs off at 30 s. These tests use a scripted
/// handler with millisecond-scale budgets.
/// </summary>
public class StreamingTimeoutTests
{
    private static TranslationBackendOptions Configured { get; } =
        new("http://probe.test/v1", "probe-model", "probe-key");

    private static string Sse(params string[] pieces)
        => string.Join("", pieces.Select(piece => $"data: {piece}\n\n")) + "data: [DONE]\n\n";

    private static string Chunk(int index)
        => $"{{\"choices\":[{{\"delta\":{{\"content\":\"{index}\"}}}}]}}";

    /// <summary>
    /// Serves SSE chunks one read at a time, with a delay between chunks —
    /// so the gap lands between reads, not before the response starts.
    /// </summary>
    private sealed class ScriptedHandler(TimeSpan preHeaders, TimeSpan perChunk, string body) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellation)
        {
            if (preHeaders > TimeSpan.Zero)
            {
                await Task.Delay(preHeaders, cancellation);
            }

            var chunks = body.Split("{{SPLIT}}", StringSplitOptions.RemoveEmptyEntries)
                .Select(System.Text.Encoding.UTF8.GetBytes)
                .ToArray();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new ChunkedStream(chunks, perChunk)),
            };
        }
    }

    private sealed class ChunkedStream(byte[][] chunks, TimeSpan gap) : Stream
    {
        private int _index = -1;
        private int _offset;
        private bool _done;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellation)
        {
            if (_done)
            {
                return 0;
            }

            if (_index < 0 || _offset >= chunks[_index].Length)
            {
                if (_index + 1 >= chunks.Length)
                {
                    _done = true;
                    return 0;
                }

                // Between chunks only — the first byte must not be delayed
                // twice (the handler's preHeaders already models that).
                if (_index >= 0 && gap > TimeSpan.Zero)
                {
                    await Task.Delay(gap, cancellation);
                }

                _index++;
                _offset = 0;
            }

            var take = Math.Min(buffer.Length, chunks[_index].Length - _offset);
            chunks[_index].AsSpan(_offset, take).CopyTo(buffer.Span);
            _offset += take;
            return take;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }

    private static async Task<List<string>> Drain(
        OpenAiCompatibleBackend backend, CancellationToken cancellation = default)
    {
        var pieces = new List<string>();
        await foreach (var piece in backend.StreamAsync(
            new ModelRequest("sys", "user"), cancellation))
        {
            pieces.Add(piece);
        }

        return pieces;
    }

    [Fact]
    public async Task A_slow_but_steady_stream_runs_longer_than_the_first_byte_budget()
    {
        // 20 events × 60 ms = 1.2 s total against a 300 ms first-byte budget:
        // only possible when the budget bounds waiting, not length.
        var body = string.Join("{{SPLIT}}",
            Enumerable.Range(0, 20).Select(i => $"data: {Chunk(i)}\n\n"));
        using var backend = new OpenAiCompatibleBackend(
            Configured,
            httpClient: new HttpClient(new ScriptedHandler(TimeSpan.Zero, TimeSpan.FromMilliseconds(60), body)),
            firstByteTimeout: TimeSpan.FromMilliseconds(300),
            idleTimeout: TimeSpan.FromMilliseconds(500));

        var pieces = await Drain(backend);

        Assert.Equal(20, pieces.Count);
    }

    [Fact]
    public async Task Headers_slower_than_the_first_byte_budget_time_out()
    {
        using var backend = new OpenAiCompatibleBackend(
            Configured,
            httpClient: new HttpClient(new ScriptedHandler(
                TimeSpan.FromMilliseconds(500), TimeSpan.Zero, Sse(Chunk(0)))),
            firstByteTimeout: TimeSpan.FromMilliseconds(80),
            idleTimeout: TimeSpan.FromSeconds(5));

        var failure = await Assert.ThrowsAsync<TranslationFailedException>(
            () => Drain(backend));

        Assert.Contains("超时", failure.Message);
    }

    [Fact]
    public async Task A_stream_that_stalls_between_chunks_times_out_mid_flight()
    {
        // First chunk arrives fast; the gap before the second exceeds the
        // idle budget.
        var body = Chunk(0) + "{{SPLIT}}" + Chunk(1);
        using var backend = new OpenAiCompatibleBackend(
            Configured,
            httpClient: new HttpClient(new ScriptedHandler(
                TimeSpan.Zero, TimeSpan.FromMilliseconds(700), body)),
            firstByteTimeout: TimeSpan.FromSeconds(5),
            idleTimeout: TimeSpan.FromMilliseconds(120));

        var failure = await Assert.ThrowsAsync<TranslationFailedException>(
            () => Drain(backend));

        Assert.Contains("超时", failure.Message);
    }

    [Fact]
    public async Task An_injected_client_survives_the_backend_that_borrowed_it()
    {
        var shared = new HttpClient(new ScriptedHandler(
            TimeSpan.Zero, TimeSpan.Zero, Sse(Chunk(0), Chunk(1))));

        using (var first = new OpenAiCompatibleBackend(Configured, httpClient: shared))
        {
            await Drain(first); // Dispose must not close the shared client.
        }

        using var second = new OpenAiCompatibleBackend(Configured, httpClient: shared);
        var pieces = await Drain(second);

        Assert.Equal(2, pieces.Count);
    }

    [Fact]
    public async Task The_callers_budget_still_cancels_the_whole_stream()
    {
        var body = string.Join("{{SPLIT}}", Enumerable.Range(0, 100).Select(Chunk));
        using var backend = new OpenAiCompatibleBackend(
            Configured,
            httpClient: new HttpClient(new ScriptedHandler(TimeSpan.Zero, TimeSpan.FromMilliseconds(50), body)),
            firstByteTimeout: TimeSpan.FromSeconds(5),
            idleTimeout: TimeSpan.FromSeconds(5));

        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Drain(backend, budget.Token));
    }
}
