namespace Shiyu.Core;

/// <summary>
/// 词典回退链：快路没有卡（含预算取消、端点不可达、词典无此词）就走慢路，
/// 慢路也没有才算真的没有卡。
///
/// 存在的动机是一个实测事实（2026-09-27 用户首测暴露）：dictionaryapi.dev
/// 在部分网络（如本机所在网络）完全不可达——三次实测全部 8 秒超时。票 35 的
/// 600ms 预算在这种网络里等于"永远没有卡"。回退把英文单词卡交给 LLM 词典化
/// （秒级、走用户自备后端），而不是降级为无。
/// </summary>
public sealed class FallbackDictionary(IDictionaryApi primary, IDictionaryApi? secondary)
    : IDictionaryApi, IDisposable
{
    public async Task<DictionaryCard?> LookupAsync(string word, CancellationToken cancellation = default)
    {
        if (await Quietly(primary, word, cancellation) is { } fast)
        {
            return fast;
        }

        return secondary is null ? null : await Quietly(secondary, word, cancellation);
    }

    /// <summary>与家族契约一致：任何失败都只是没有卡，绝不抛。</summary>
    private static async Task<DictionaryCard?> Quietly(
        IDictionaryApi api, string word, CancellationToken cancellation)
    {
        try
        {
            return await api.LookupAsync(word, cancellation);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        (primary as IDisposable)?.Dispose();
        (secondary as IDisposable)?.Dispose();
    }
}
