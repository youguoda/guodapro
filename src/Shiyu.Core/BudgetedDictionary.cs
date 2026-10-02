namespace Shiyu.Core;

/// <summary>
/// 两阶段词典的预算外壳：到期没等到就静默放弃。
///
/// 600ms 是票面钦定的预算——翻译已经先出了，词典细节是补上的那一刀，
/// 迟到的卡不如没有的卡。预算挂在链接取消源上，调用方自己的取消照常
/// 生效；内层的任何结果与异常到达时若预算已过，也只是“没有卡”。
/// </summary>
public sealed class BudgetedDictionary(IDictionaryApi inner, TimeSpan? budget = null)
    : IDictionaryApi, IDisposable
{
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromMilliseconds(600);

    public async Task<DictionaryCard?> LookupAsync(string word, CancellationToken cancellation = default)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        window.CancelAfter(budget ?? DefaultBudget);

        try
        {
            return await inner.LookupAsync(word, window.Token);
        }
        catch (Exception)
        {
            // expected: 含预算到期的 OperationCanceledException——静默放弃
            // 是预算这条腿的契约。
            return null;
        }
    }

    /// <summary>
    /// 面板对每次查词组装的端口按次释放；内层的 HttpClient（免费 API）与
    /// 模型连接（LLM API）跟着内层一起走，而不是等 GC 收捡套接字。
    /// </summary>
    public void Dispose() => (inner as IDisposable)?.Dispose();
}
