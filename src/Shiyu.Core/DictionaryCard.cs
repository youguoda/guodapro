namespace Shiyu.Core;

/// <summary>词典卡的一条义项：词性、释义、例句、同义词。</summary>
/// <param name="PartOfSpeech">词性（free 词典给英文，LLM 兜底给中文）。</param>
public sealed record DictionarySense(
    string? PartOfSpeech,
    IReadOnlyList<string> Definitions,
    IReadOnlyList<string> Examples,
    IReadOnlyList<string> Synonyms);

/// <summary>
/// 单词词典卡：查词场景从 LLM 泛答升级出来的那个“词典精度”的载体。
///
/// 它天生是紧凑卡——面板里一屏读完，不是词典网站的词条页。上限写进
/// 模型而不是渲染层，任何调用方拿到的都是同一张克制的卡。
/// </summary>
/// <param name="Phonetic">音标（英文 IPA；中文词是带调拼音）。查不到为 null。</param>
public sealed record DictionaryCard(
    string Word,
    string? Phonetic,
    IReadOnlyList<DictionarySense> Senses)
{
    public const int MaxSenses = 4;
    public const int MaxDefinitionsPerSense = 3;
    public const int MaxExamplesPerSense = 2;
    public const int MaxSynonymsPerSense = 4;
}

/// <summary>
/// 词典端口。查不到、超预算、网络坏、响应烂——对外一律是 null：词典卡
/// 是锦上添花，“没有卡”是它唯一允许的失败形态，永远不该变成报错。
/// </summary>
public interface IDictionaryApi
{
    Task<DictionaryCard?> LookupAsync(string word, CancellationToken cancellation = default);
}
