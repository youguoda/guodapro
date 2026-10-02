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
    HttpClient? httpClient = null,
    Func<TimeSpan>? transientBackoff = null) : ITranslationBackend, IStreamingModel, IDisposable
{
    /// <summary>瞬态失败最多退避重试两次（即整发三次）。</summary>
    private const int MaxAttempts = 3;

    private readonly HttpClient _http = httpClient ?? new HttpClient();
    private readonly bool _ownsClient = httpClient is null;
    private readonly Func<TimeSpan> _transientBackoff = transientBackoff ?? NextTransientBackoff;

    /// <summary>
    /// 抽样一次默认退避时长。窗口 [600,1400]ms 取自 Glossy 对上游瞬态
    /// 错误的实证：够对端喘口气，又不让面板干等到像卡死。
    /// </summary>
    internal static TimeSpan NextTransientBackoff()
        => TimeSpan.FromMilliseconds(Random.Shared.Next(600, 1401));

    public IAsyncEnumerable<string> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellation)
        => StreamAsync(
            new ModelRequest(TranslationPrompt.For(request), request.Text)
            {
                Temperature = request.Temperature,
            },
            cancellation);

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

        var response = await SendWithRetryAsync(request, timeout, cancellation);

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

    /// <summary>
    /// 发送并对瞬态状态码整发重试。请求消息逐次重建——HttpClient 不允许
    /// 同一条消息发两次；退避等待挂在整个超时预算上，三次尝试合计仍受
    /// 单个 Timeout 约束，面板的等待时间因此有上界。
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        ModelRequest request,
        CancellationTokenSource timeout,
        CancellationToken cancellation)
    {
        HttpResponseMessage response;
        for (var attempt = 1; ; attempt++)
        {
            using var message = BuildRequest(request);
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

            if (response.IsSuccessStatusCode
                || !IsTransient((int)response.StatusCode)
                || attempt >= MaxAttempts)
            {
                return response;
            }

            response.Dispose();

            try
            {
                await Task.Delay(_transientBackoff(), timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                // 重试还没发出去预算先耗尽：这与首射超时对用户是同一件事。
                throw new TranslationFailedException("翻译服务响应超时。");
            }
        }
    }

    /// <summary>
    /// 瞬态状态码：限流（429）、过载（529，DeepSeek/Anthropic 系）与常见
    /// 网关故障。凭据、余额、路径类错误重试一万次也不会变好，不在此列。
    /// </summary>
    internal static bool IsTransient(int statusCode)
        => statusCode is 408 or 429 or 500 or 502 or 503 or 504 or 529;

    private HttpRequestMessage BuildRequest(ModelRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["stream"] = true,

            // Translation wants the likeliest rendering, not an interesting one.
            ["temperature"] = request.Temperature,
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
    /// echoing the credential back at them. 凭据与余额类终态错误只给人话：
    /// 服务商响应体里的原文对用户既不可读也不可行动。
    /// </summary>
    private static async Task<string> DescribeFailure(
        HttpResponseMessage response, CancellationToken cancellation)
    {
        var status = (int)response.StatusCode;
        var terminal = status is 401 or 402 or 403;

        var reason = status switch
        {
            401 or 403 => "凭据无效或已过期，请检查设置里的接口密钥",
            402 => "账户余额不足，请到服务商处充值",
            404 => "接口地址或模型名不正确",
            429 => "请求过于频繁，稍后再试",
            >= 500 => "翻译服务暂时不可用",
            _ => $"翻译服务返回 {status}",
        };

        if (terminal)
        {
            return $"翻译失败：{reason}。";
        }

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
            // expected: 读错误体失败——状态码本身仍值得报告。
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
