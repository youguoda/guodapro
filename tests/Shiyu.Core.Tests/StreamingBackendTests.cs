using System.Net;
using System.Text;
using System.Text.Json.Nodes;
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

/// <summary>
/// 记录每次发出的请求并按调用序号回放应答——后端的错误码与重试行为
/// 全靠它从公共接口观察。
/// </summary>
public sealed class ScriptedHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Message, string? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellation)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellation);
        Requests.Add((request, body));

        return respond(Requests.Count);
    }
}

public static class BackendTransport
{
    public static TranslationBackendOptions Configured()
        => new("https://api.example.com/v1", "some-model", "some-key");

    public static HttpResponseMessage Status(int code)
        => new((HttpStatusCode)code);

    /// <summary>一次成功且立即结束的 SSE 应答。</summary>
    public static HttpResponseMessage Stream(params string[] frames)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                string.Concat(frames.Select(frame => $"data: {frame}\n\n")),
                Encoding.UTF8,
                "text/event-stream"),
        };
}

public class BackendTemperatureTests
{
    [Fact]
    public void The_default_temperature_is_the_low_one_Glossy_validated()
    {
        // 独立事实来源是票面钦定的 0.2，不是实现里的常量本身。
        Assert.Equal(0.2, TranslationPrompt.DefaultTemperature);
    }

    [Fact]
    public void A_translation_request_carries_the_low_temperature_by_default()
    {
        Assert.Equal(0.2, new TranslationRequest("hello", "Chinese").Temperature);
        Assert.Equal(0.7, new TranslationRequest("hello", "Chinese") { Temperature = 0.7 }.Temperature);
    }

    [Fact]
    public async Task The_default_temperature_reaches_the_wire()
    {
        var handler = new ScriptedHandler(_ =>
            BackendTransport.Stream("""{"choices":[{"delta":{"content":"你好"}}]}""", "[DONE]"));
        var backend = new OpenAiCompatibleBackend(
            BackendTransport.Configured(), new HttpClient(handler));

        await foreach (var _ in backend.TranslateAsync(
            new TranslationRequest("hello", "Chinese"), CancellationToken.None))
        {
        }

        var body = JsonNode.Parse(handler.Requests.Single().Body!)!;
        Assert.Equal(0.2, (double)body["temperature"]!);
    }

    [Fact]
    public async Task An_overridden_temperature_reaches_the_wire_instead()
    {
        // 覆盖口：调用方要发散（比如日后的写作辅助）时不必改后端。
        var handler = new ScriptedHandler(_ =>
            BackendTransport.Stream("""{"choices":[{"delta":{"content":"你好"}}]}""", "[DONE]"));
        var backend = new OpenAiCompatibleBackend(
            BackendTransport.Configured(), new HttpClient(handler));

        await foreach (var _ in backend.TranslateAsync(
            new TranslationRequest("hello", "Chinese") { Temperature = 0.7 },
            CancellationToken.None))
        {
        }

        var body = JsonNode.Parse(handler.Requests.Single().Body!)!;
        Assert.Equal(0.7, (double)body["temperature"]!);
    }
}

public class BackendFailureHandlingTests
{
    /// <summary>记录被询问的退避时长，且不真等——测试要的是次数不是延迟。</summary>
    private static Func<TimeSpan> ZeroBackoff(List<TimeSpan>? asked = null)
        => () =>
        {
            asked?.Add(TimeSpan.Zero);
            return TimeSpan.Zero;
        };

    private static OpenAiCompatibleBackend Backend(
        ScriptedHandler handler, Func<TimeSpan>? backoff = null)
        => new(BackendTransport.Configured(), new HttpClient(handler), backoff);

    private static async Task Drain(
        TranslationRequest request, OpenAiCompatibleBackend backend)
    {
        await foreach (var _ in backend.TranslateAsync(request, CancellationToken.None))
        {
        }
    }

    private static Task<TranslationFailedException> Fails(
        TranslationRequest request, OpenAiCompatibleBackend backend)
        => Assert.ThrowsAsync<TranslationFailedException>(() => Drain(request, backend));

    private static TranslationRequest Request => new("hello", "Chinese");

    [Fact]
    public async Task A_transient_outage_is_retried_twice_and_can_recover()
    {
        // 首射 503、重试 503、再重试成功：整条译文照常流回。
        var handler = new ScriptedHandler(call => call switch
        {
            1 or 2 => BackendTransport.Status(503),
            _ => BackendTransport.Stream(
                """{"choices":[{"delta":{"content":"你好"}}]}""", "[DONE]"),
        });
        var backend = Backend(handler, ZeroBackoff());

        var pieces = new List<string>();
        await foreach (var piece in backend.TranslateAsync(Request, CancellationToken.None))
        {
            pieces.Add(piece);
        }

        Assert.Equal(["你好"], pieces);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(529)]
    public async Task Transient_statuses_get_exactly_two_retries(int status)
    {
        var handler = new ScriptedHandler(_ => BackendTransport.Status(status));
        var backend = Backend(handler, ZeroBackoff());

        await Fails(Request, backend);

        // 首射 + 两次退避重试，到此为止。
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(402)]
    [InlineData(404)]
    public async Task Terminal_statuses_are_never_retried(int status)
    {
        var handler = new ScriptedHandler(_ => BackendTransport.Status(status));
        var backend = Backend(handler, ZeroBackoff());

        await Fails(Request, backend);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_persistent_rate_limit_reports_a_human_message_after_its_retries()
    {
        var asked = new List<TimeSpan>();
        var handler = new ScriptedHandler(_ => BackendTransport.Status(429));
        var backend = Backend(handler, ZeroBackoff(asked));

        var failure = await Fails(Request, backend);

        Assert.Contains("请求过于频繁", failure.Message);
        Assert.Equal(2, asked.Count);
    }

    [Fact]
    public async Task A_server_error_reports_a_human_message_after_its_retries()
    {
        var handler = new ScriptedHandler(_ => BackendTransport.Status(503));
        var backend = Backend(handler, ZeroBackoff());

        var failure = await Fails(Request, backend);

        Assert.Contains("翻译服务暂时不可用", failure.Message);
    }

    [Fact]
    public async Task An_invalid_credential_is_a_plain_message_without_the_raw_body()
    {
        // 凭据类错误直接给人话：把服务商的 JSON 原文贴给用户是二次伤害。
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """{"error":{"message":"Invalid API key provided"}}""",
                Encoding.UTF8,
                "application/json"),
        });
        var backend = Backend(handler, ZeroBackoff());

        var failure = await Fails(Request, backend);

        Assert.Contains("凭据无效", failure.Message);
        Assert.DoesNotContain("Invalid API key", failure.Message);
    }

    [Fact]
    public async Task An_out_of_balance_account_is_told_so_in_plain_words()
    {
        var handler = new ScriptedHandler(_ => BackendTransport.Status(402));
        var backend = Backend(handler, ZeroBackoff());

        var failure = await Fails(Request, backend);

        Assert.Contains("余额不足", failure.Message);
    }

    [Fact]
    public async Task A_wrong_endpoint_or_model_name_is_reported_without_retrying()
    {
        var handler = new ScriptedHandler(_ => BackendTransport.Status(404));
        var backend = Backend(handler, ZeroBackoff());

        var failure = await Fails(Request, backend);

        Assert.Contains("接口地址或模型名不正确", failure.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void The_default_backoff_stays_inside_the_validated_window()
    {
        // [600,1400]ms 是 Glossy 实证的窗口：够让对端喘口气，又不至于
        // 让面板干等。抽样钉住边界，不钉具体值。
        var seen = new HashSet<int>();
        for (var i = 0; i < 300; i++)
        {
            var delay = OpenAiCompatibleBackend.NextTransientBackoff();
            Assert.InRange(delay.TotalMilliseconds, 600, 1400);
            seen.Add((int)delay.TotalMilliseconds);
        }

        Assert.True(seen.Count > 1);
    }
}
