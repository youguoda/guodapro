using System.Net.Http;
using System.Net.Sockets;
using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// 票 22 / UI 报告 §6.2 错误文案映射：异常 →（标题，说明）全表。
/// 断言两件事：类别判得对，且标题与说明永远不含 .NET 英文原文
/// （探针 defect:panel-raw-english-error 的编译期版本）。
/// </summary>
public class TranslationUserErrorTests
{
    private static string TitleAndDetail(TranslationUserError error) => error.Title + "|" + error.Detail;

    [Fact]
    public void Tls_failures_map_to_the_security_row()
    {
        // 抓包工具/自签证书的最常见脸：HttpRequestException 包着认证失败。
        var inner = new IOException("The SSL connection could not be established, see inner exception.");
        var wrapped = new TranslationFailedException(
            "连接翻译服务失败：The SSL connection could not be established.", new HttpRequestException("boom", inner));

        var error = TranslationUserErrorMapper.Describe(wrapped, wrapped.Message);

        Assert.Equal(TranslationErrorKind.Security, error.Kind);
        Assert.Equal("无法建立安全连接", error.Title);
        Assert.Equal("可能是代理、抓包工具或系统时间导致证书校验失败。", error.Detail);
        Assert.True(error.ShowRetry);
        Assert.True(error.ShowSettings);
    }

    [Fact]
    public void Timeout_messages_map_to_the_timeout_row()
    {
        var error = TranslationUserErrorMapper.Describe(
            new TranslationFailedException("翻译服务响应超时。"), "翻译服务响应超时。");

        Assert.Equal(TranslationErrorKind.Timeout, error.Kind);
        Assert.Equal("服务响应超时|网络较慢或服务繁忙。", TitleAndDetail(error));
        Assert.True(error.ShowRetry);
        Assert.False(error.ShowSettings);
    }

    [Fact]
    public void Network_failures_map_to_the_network_row()
    {
        var socketError = new SocketException();
        var wrapped = new TranslationFailedException(
            "连接翻译服务失败：No such host is known.", new HttpRequestException("No such host is known.", socketError));

        var error = TranslationUserErrorMapper.Describe(wrapped, wrapped.Message);

        Assert.Equal(TranslationErrorKind.Network, error.Kind);
        Assert.Equal("无法连接到翻译服务", error.Title);
        Assert.Equal("请检查网络连接。", error.Detail);
        Assert.True(error.ShowRetry);
    }

    [Fact]
    public void Credential_failures_map_to_the_auth_row_without_retry()
    {
        var error = TranslationUserErrorMapper.Describe(
            new TranslationFailedException("翻译失败：凭据无效或已过期，请检查设置里的接口密钥。"),
            "翻译失败：凭据无效或已过期，请检查设置里的接口密钥。");

        Assert.Equal(TranslationErrorKind.Auth, error.Kind);
        Assert.Equal("密钥无效或没有权限", error.Title);
        Assert.Equal("请在设置中检查 API 密钥。", error.Detail);
        Assert.False(error.ShowRetry);
        Assert.True(error.ShowSettings);
    }

    [Fact]
    public void Rate_limit_failures_map_to_the_rate_limit_row()
    {
        var error = TranslationUserErrorMapper.Describe(
            new TranslationFailedException("翻译失败：请求过于频繁，稍后再试。"),
            "翻译失败：请求过于频繁，稍后再试。");

        Assert.Equal(TranslationErrorKind.RateLimit, error.Kind);
        Assert.Equal("请求过于频繁|稍等片刻再试。", TitleAndDetail(error));
        Assert.True(error.ShowRetry);
    }

    [Fact]
    public void Machine_made_messages_fall_back_to_the_unexpected_row()
    {
        // 探针场景：无 scheme 的地址，HttpClient 抛出纯英文 InvalidOperationException。
        var boom = new InvalidOperationException(
            "An invalid request URI was provided. The request URI must either be an absolute URI or BaseAddress must be set.");

        var error = TranslationUserErrorMapper.Describe(boom, boom.Message);

        Assert.Equal(TranslationErrorKind.Other, error.Kind);
        Assert.Equal("翻译失败", error.Title);
        Assert.Equal("服务返回了意外的响应。", error.Detail);
        Assert.Contains("InvalidOperationException", error.Raw);
        Assert.DoesNotMatch("[A-Za-z]{2,}(?: [a-z]+){2,}", error.Title + error.Detail);
    }

    [Theory]
    [InlineData("翻译失败：账户余额不足，请到服务商处充值。", "账户余额不足，请到服务商处充值。")]
    [InlineData("翻译失败：免费额度已用完，请实名或充值。", "免费额度已用完，请实名或充值。")]
    [InlineData("还没有配置翻译后端：请先在设置中填写接口地址、模型与凭据。", "还没有配置翻译后端：请先在设置中填写接口地址、模型与凭据。")]
    [InlineData("单次最多翻译 2000 字，请把文本分段后再试。", "单次最多翻译 2000 字，请把文本分段后再试。")]
    public void Human_messages_keep_their_own_words(string message, string expectedDetail)
    {
        var error = TranslationUserErrorMapper.Describe(new TranslationFailedException(message), message);

        Assert.Equal(TranslationErrorKind.Other, error.Kind);
        Assert.Equal("翻译失败", error.Title);
        Assert.Equal(expectedDetail, error.Detail);
    }

    [Fact]
    public void The_raw_chain_carries_type_names_for_the_collapsed_details()
    {
        var inner = new HttpRequestException("No such host is known.");
        var wrapped = new TranslationFailedException("连接翻译服务失败：No such host is known.", inner);

        var raw = TranslationUserErrorMapper.Describe(wrapped, wrapped.Message).Raw;

        Assert.Contains("TranslationFailedException: 连接翻译服务失败", raw);
        Assert.Contains("HttpRequestException: No such host", raw);
    }

    [Fact]
    public async Task The_session_keeps_the_failure_for_the_panel_to_map()
    {
        var session = new TranslationSession(new ThrowingBackend(
            new InvalidOperationException("An invalid request URI was provided for the probe.")));

        await session.RunAsync(new TranslationRequest("hi", "Chinese"));

        Assert.Equal(TranslationState.Failed, session.State);
        Assert.NotNull(session.Failure);
        Assert.Equal(session.Error, session.Failure.Message);
    }

    private sealed class ThrowingBackend(Exception boom) : ITranslationBackend
    {
        // 同步抛：枚举还没开始就炸，会话的兜底 catch 把它收敛成 Failed。
        public IAsyncEnumerable<string> TranslateAsync(
            TranslationRequest request, CancellationToken cancellation)
            => throw boom;
    }
}
