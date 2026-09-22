using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shiyu.Core;

/// <param name="BaseUrl">Endpoint root, e.g. a provider's OpenAI-compatible base.</param>
/// <param name="ApiKey">Never logged, never shown in the interface.</param>
public sealed record TranslationBackendOptions(string BaseUrl, string Model, string ApiKey)
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(Model)
        && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Talks to any endpoint speaking the OpenAI chat-completions shape, which by
/// now includes most providers worth pointing this at.
///
/// Chosen over a provider-specific client so that swapping backends is a
/// settings change rather than a code change — the port exists precisely so
/// this class can be replaced without anything else noticing.
/// </summary>
public sealed class OpenAiCompatibleBackend(
    TranslationBackendOptions options,
    HttpClient? httpClient = null) : ITranslationBackend, IStreamingModel, IDisposable
{
    private readonly HttpClient _http = httpClient ?? new HttpClient();
    private readonly bool _ownsClient = httpClient is null;

    public IAsyncEnumerable<string> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellation)
        => StreamAsync(
            new ModelRequest(TranslationPrompt.For(request), request.Text), cancellation);

    /// <summary>
    /// The one transport. Translation and agent actions both come through here
    /// rather than each having its own way to reach a model.
    /// </summary>
    public async IAsyncEnumerable<string> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        if (!options.IsConfigured)
        {
            throw new TranslationFailedException("还没有配置翻译后端：请先在设置中填写接口地址、模型与凭据。");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(options.Timeout);

        using var message = BuildRequest(request);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (HttpRequestException network)
        {
            throw new TranslationFailedException($"连接翻译服务失败：{network.Message}", network);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            // The linked token fired, not the caller's: this is the timeout.
            throw new TranslationFailedException("翻译服务响应超时。");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new TranslationFailedException(await DescribeFailure(response, timeout.Token));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);

            await foreach (var payload in ServerSentEvents.ReadDataAsync(stream, timeout.Token))
            {
                if (ExtractContent(payload) is { Length: > 0 } piece)
                {
                    yield return piece;
                }
            }
        }
    }

    private HttpRequestMessage BuildRequest(ModelRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["stream"] = true,

            // Translation wants the likeliest rendering, not an interesting one.
            ["temperature"] = 0.2,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = request.SystemPrompt,
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = request.UserContent,
                },
            },
        };

        var message = new HttpRequestMessage(
            HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        return message;
    }

    /// <summary>
    /// Turns a failed response into something a user can act on, without ever
    /// echoing the credential back at them.
    /// </summary>
    private static async Task<string> DescribeFailure(
        HttpResponseMessage response, CancellationToken cancellation)
    {
        var reason = (int)response.StatusCode switch
        {
            401 or 403 => "凭据无效或已过期",
            404 => "接口地址或模型名不正确",
            429 => "请求过于频繁，稍后再试",
            >= 500 => "翻译服务暂时不可用",
            _ => $"翻译服务返回 {(int)response.StatusCode}",
        };

        string? detail = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellation);
            if (body.Length is > 0 and < 400)
            {
                detail = body;
            }
        }
        catch (Exception)
        {
            // The status code alone is still worth reporting.
        }

        return detail is null ? $"翻译失败：{reason}。" : $"翻译失败：{reason}。{detail}";
    }

    /// <summary>
    /// Pulls the text out of one streamed chunk. A chunk that does not parse is
    /// skipped rather than aborting the stream: providers interleave frames
    /// carrying usage statistics and other bookkeeping.
    /// </summary>
    internal static string? ExtractContent(string payload)
    {
        try
        {
            var choices = JsonNode.Parse(payload)?["choices"]?.AsArray();
            if (choices is null || choices.Count == 0)
            {
                return null;
            }

            var first = choices[0];
            return first?["delta"]?["content"]?.GetValue<string>()
                ?? first?["message"]?["content"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
