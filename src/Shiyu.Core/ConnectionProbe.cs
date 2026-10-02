using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Shiyu.Core;

/// <summary>「测试连接」的三种结局——人话区分，不带一个英文异常。</summary>
public enum ConnectionTestVerdict
{
    /// <summary>请求走通了：地址对、密钥对、模型对（附耗时）。</summary>
    Success,

    /// <summary>服务商拒绝了凭据（401/403）。</summary>
    InvalidKey,

    /// <summary>连不上、超时，或地址/模型名不对（404 等）。</summary>
    Unreachable,
}

/// <param name="Elapsed">Success 时的耗时，给用户看"这条线路值不值得"。</param>
/// <param name="Message">人话说明；null 表示按 Verdict 说默认的那句。</param>
public sealed record ConnectionTestOutcome(
    ConnectionTestVerdict Verdict,
    TimeSpan? Elapsed = null,
    string? Message = null);

/// <summary>
/// 「测试连接」（票 08）：对着用户刚填的地址发一个极小的真实请求
/// （"hi" 译中文、非流式、max_tokens 压到最小），一次性问出三件事——
/// 地址通不通、密钥认不认、模型在不在。
///
/// 与真翻译同一个请求体形状（含预设的附加字段与温度规则），所以它
/// 验证的就是之后每次翻译要走的路。不重试：测试的意义是看清第一次
/// 尝试的结局。密钥只进 Authorization 头，不进任何文案。
/// </summary>
public static class ConnectionProbe
{
    private const int ProbeMaxTokens = 16;

    public static async Task<ConnectionTestOutcome> TestAsync(
        TranslationBackendOptions options,
        ProviderPreset? preset = null,
        HttpClient? httpClient = null,
        CancellationToken cancellation = default)
    {
        if (!options.IsConfigured)
        {
            return new ConnectionTestOutcome(
                ConnectionTestVerdict.Unreachable,
                Message: "请先填好服务地址、模型与密钥，再测试连接。");
        }

        using var owned = httpClient is null ? new HttpClient() : null;
        var http = httpClient ?? owned!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(options.Timeout);

        using var message = BuildRequest(options, preset);
        var watch = Stopwatch.StartNew();
        try
        {
            using var response = await http.SendAsync(message, timeout.Token);
            watch.Stop();
            return await Classify(response, watch.Elapsed, timeout.Token);
        }
        catch (HttpRequestException)
        {
            return new ConnectionTestOutcome(
                ConnectionTestVerdict.Unreachable,
                Message: "连不上服务地址——请检查地址拼写与网络。");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return new ConnectionTestOutcome(
                ConnectionTestVerdict.Unreachable,
                Message: "连接超时——地址可能不正确，或网络不通。");
        }
    }

    private static HttpRequestMessage BuildRequest(
        TranslationBackendOptions options, ProviderPreset? preset)
    {
        var body = new JsonObject
        {
            ["model"] = options.Model,
            ["stream"] = false,
            ["max_tokens"] = ProbeMaxTokens,
            ["temperature"] = Math.Min(
                TranslationPrompt.DefaultTemperature,
                preset?.MaxTemperature ?? TranslationPrompt.DefaultTemperature),
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = "hi",
                },
            },
        };

        if (preset is { SendTemperature: false })
        {
            body.Remove("temperature");
        }

        if (preset?.ExtraBody is { Count: > 0 })
        {
            foreach (var (name, value) in preset.ExtraBody)
            {
                body[name] = value?.DeepClone();
            }
        }

        var message = new HttpRequestMessage(
            HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        return message;
    }

    private static async Task<ConnectionTestOutcome> Classify(
        HttpResponseMessage response, TimeSpan elapsed, CancellationToken cancellation)
    {
        var status = (int)response.StatusCode;

        if (status is >= 200 and < 300)
        {
            return new ConnectionTestOutcome(ConnectionTestVerdict.Success, elapsed);
        }

        // 百炼的 403 FreeTierOnly：地址、密钥、模型全对，拒绝的是额度。
        // 这是"连上了但要注意"——归入成功并明说，别把用户支去改密钥。
        if (status is 401 or 403)
        {
            if (status == 403 && await MentionsFreeTierOnly(response, cancellation))
            {
                return new ConnectionTestOutcome(
                    ConnectionTestVerdict.Success, elapsed,
                    Message: "连接成功，密钥有效；但免费额度已用完——实名或充值后即可正常翻译。");
            }

            return new ConnectionTestOutcome(
                ConnectionTestVerdict.InvalidKey,
                Message: "密钥无效或已过期（服务商返回 "
                    + status + "）。请核对密钥是否复制完整。");
        }

        return new ConnectionTestOutcome(
            ConnectionTestVerdict.Unreachable,
            Message: status == 404
                ? "地址或模型名不正确（404）。请检查服务地址与模型。"
                : $"服务暂时不可用（{status}），稍后再试。");
    }

    private static async Task<bool> MentionsFreeTierOnly(
        HttpResponseMessage response, CancellationToken cancellation)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellation);
            return body.Contains("AllocationQuota.FreeTierOnly", StringComparison.Ordinal);
        }
        catch (Exception)
        {
            // 读不出错误体就当普通凭据错误处理：状态码本身仍值得报告。
            return false;
        }
    }
}
