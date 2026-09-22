using System.Text;
using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class ServerSentEventsTests
{
    private static Stream StreamOf(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static async Task<List<string>> ReadAll(string text)
    {
        var payloads = new List<string>();
        await foreach (var payload in ServerSentEvents.ReadDataAsync(StreamOf(text)))
        {
            payloads.Add(payload);
        }

        return payloads;
    }

    [Fact]
    public async Task Data_lines_are_returned_in_order()
    {
        var payloads = await ReadAll("data: one\n\ndata: two\n\ndata: three\n\n");

        Assert.Equal(new[] { "one", "two", "three" }, payloads);
    }

    [Fact]
    public async Task The_done_sentinel_ends_the_stream()
    {
        var payloads = await ReadAll("data: one\n\ndata: [DONE]\n\ndata: never\n\n");

        Assert.Equal(new[] { "one" }, payloads);
    }

    [Fact]
    public async Task Comment_frames_and_blank_lines_are_ignored()
    {
        // Providers send comment lines as keep-alives on a quiet connection.
        var payloads = await ReadAll(": keep-alive\n\ndata: one\n\n\n: another\n\ndata: two\n\n");

        Assert.Equal(new[] { "one", "two" }, payloads);
    }

    [Fact]
    public async Task A_stream_that_stops_without_the_sentinel_simply_ends()
    {
        var payloads = await ReadAll("data: one\n\ndata: two");

        Assert.Equal(new[] { "one", "two" }, payloads);
    }

    [Fact]
    public async Task Lines_that_are_not_data_frames_are_skipped()
    {
        var payloads = await ReadAll("event: message\ndata: one\n\nid: 7\ndata: two\n\n");

        Assert.Equal(new[] { "one", "two" }, payloads);
    }
}

public class ChunkExtractionTests
{
    [Fact]
    public void The_text_of_a_delta_chunk_is_extracted()
    {
        const string chunk = """
            {"choices":[{"delta":{"content":"你好"},"index":0}]}
            """;

        Assert.Equal("你好", OpenAiCompatibleBackend.ExtractContent(chunk));
    }

    [Fact]
    public void A_non_streaming_message_chunk_is_understood_too()
    {
        const string chunk = """
            {"choices":[{"message":{"content":"你好"},"index":0}]}
            """;

        Assert.Equal("你好", OpenAiCompatibleBackend.ExtractContent(chunk));
    }

    [Fact]
    public void A_chunk_with_no_content_yields_nothing_rather_than_throwing()
    {
        // Providers interleave frames carrying only a finish reason or usage
        // figures. Treating one as fatal would truncate a good translation.
        Assert.Null(OpenAiCompatibleBackend.ExtractContent(
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}"""));

        Assert.Null(OpenAiCompatibleBackend.ExtractContent(
            """{"usage":{"total_tokens":42}}"""));
    }

    [Fact]
    public void Malformed_json_is_skipped_rather_than_ending_the_stream()
        => Assert.Null(OpenAiCompatibleBackend.ExtractContent("{not json at all"));

    [Fact]
    public void An_empty_choices_array_yields_nothing()
        => Assert.Null(OpenAiCompatibleBackend.ExtractContent("""{"choices":[]}"""));
}

public class BackendConfigurationTests
{
    [Fact]
    public async Task An_unconfigured_backend_says_so_instead_of_failing_obscurely()
    {
        var backend = new OpenAiCompatibleBackend(new TranslationBackendOptions("", "", ""));
        var session = new TranslationSession(backend);

        await session.RunAsync(new TranslationRequest("hello", "Chinese"));

        Assert.Equal(TranslationState.Failed, session.State);
        Assert.Contains("还没有配置", session.Error);
    }

    [Theory]
    [InlineData("", "model", "key")]
    [InlineData("https://example.com/v1", "", "key")]
    [InlineData("https://example.com/v1", "model", "")]
    public void Every_field_is_required_before_a_backend_counts_as_configured(
        string baseUrl, string model, string apiKey)
        => Assert.False(new TranslationBackendOptions(baseUrl, model, apiKey).IsConfigured);

    [Fact]
    public void A_fully_filled_in_backend_counts_as_configured()
        => Assert.True(
            new TranslationBackendOptions("https://example.com/v1", "some-model", "some-key")
                .IsConfigured);
}
