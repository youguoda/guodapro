using System.Net.Http;
using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class FallbackDictionaryTests
{
    private static readonly DictionaryCard Card = new(
        "word", "/wɜːd/", [new DictionarySense("名词", ["a unit"], ["use it"], ["term"])]);

    private sealed class Scripted : IDictionaryApi, IDisposable
    {
        private readonly Func<string, DictionaryCard?> _onLookup;

        public int Lookups { get; private set; }

        public bool Disposed { get; private set; }

        public Scripted(Func<string, DictionaryCard?> onLookup) => _onLookup = onLookup;

        public Task<DictionaryCard?> LookupAsync(string word, CancellationToken cancellation = default)
        {
            Lookups++;
            return Task.FromResult(_onLookup(word));
        }

        public void Dispose() => Disposed = true;
    }

    private static DictionaryCard? Null(string _) => null;

    [Fact]
    public async Task The_fast_path_answer_wins_and_the_slow_path_stays_home()
    {
        var slow = new Scripted(Null);
        using var chain = new FallbackDictionary(new Scripted(_ => Card), slow);

        var card = await chain.LookupAsync("word");

        Assert.Same(Card, card);
        Assert.Equal(0, slow.Lookups);
    }

    [Fact]
    public async Task An_unreachable_fast_path_falls_through_to_the_slow_one()
    {
        // 实测场景：dictionaryapi.dev 不可达/预算取消都表现为 null。
        using var chain = new FallbackDictionary(new Scripted(Null), new Scripted(_ => Card));

        var card = await chain.LookupAsync("word");

        Assert.Same(Card, card);
    }

    [Fact]
    public async Task A_throwing_fast_path_is_silence_not_an_error()
    {
        var throwing = new Scripted(_ => throw new HttpRequestException("unreachable"));
        using var chain = new FallbackDictionary(throwing, new Scripted(_ => Card));

        var card = await chain.LookupAsync("word");

        Assert.Same(Card, card);
    }

    [Fact]
    public async Task No_card_anywhere_means_no_card()
    {
        using var chain = new FallbackDictionary(new Scripted(Null), new Scripted(Null));

        Assert.Null(await chain.LookupAsync("word"));
    }

    [Fact]
    public async Task A_missing_slow_path_degrades_to_the_fast_one_alone()
    {
        // 没有自备密钥的用户：慢路缺席，行为退回票 35 原状（免费路独走）。
        using var chain = new FallbackDictionary(new Scripted(Null), null);

        Assert.Null(await chain.LookupAsync("word"));
    }

    [Fact]
    public async Task The_real_shape_a_budgeted_free_api_over_an_unreachable_network()
    {
        // 组装即接线：预算外壳套一个永远抛取消的端口（模拟 600ms 到期），
        // 回退链后面站一个 LLM 卡——用户网络里英文单词卡由它接住。
        var never = new BudgetedDictionary(new Scripted(_ => throw new OperationCanceledException()));
        using var chain = new FallbackDictionary(never, new Scripted(_ => Card));

        Assert.Same(Card, await chain.LookupAsync("word"));
    }

    [Fact]
    public void Disposal_reaches_both_inner_ports()
    {
        var fast = new Scripted(_ => Card);
        var slow = new Scripted(_ => Card);
        var chain = new FallbackDictionary(fast, slow);

        chain.Dispose();

        Assert.True(fast.Disposed);
        Assert.True(slow.Disposed);
    }

    [Fact]
    public void The_budget_shell_releases_its_inner_port_on_dispose()
    {
        var inner = new Scripted(_ => Card);
        using var budgeted = new BudgetedDictionary(inner);

        budgeted.Dispose();

        Assert.True(inner.Disposed);
    }
}
