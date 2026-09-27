using System.Text;
using System.Text.Json.Nodes;

namespace Shiyu.Core;

/// <summary>
/// 中文词的词典兜底：复用翻译的模型通道，用词典化 prompt 换一张卡回来。
///
/// dictionaryapi.dev 只有英文；中文词的词典细节本来就要靠模型——所以
/// 兜底不是降级，而是这条路唯一的走法。流式攒齐再解析，坏输出静默放弃，
/// 与 <see cref="IDictionaryApi"/> 的契约一致。
/// </summary>
public sealed class LlmDictionaryApi(IStreamingModel model) : IDictionaryApi, IDisposable
{
    public async Task<DictionaryCard?> LookupAsync(string word, CancellationToken cancellation = default)
    {
        try
        {
            var builder = new StringBuilder();
            await foreach (var piece in model.StreamAsync(
                new ModelRequest(DictionaryPrompt.SystemPrompt(), DictionaryPrompt.UserContent(word)),
                cancellation))
            {
                builder.Append(piece);
            }

            return Parse(builder.ToString(), word);
        }
        catch (Exception)
        {
            // 模型不可用与模型胡说对查词者是同一件事：没有卡。
            return null;
        }
    }

    /// <summary>从模型输出里抠出 JSON 再拼卡；抠不出就是 null。</summary>
    private static DictionaryCard? Parse(string output, string word)
    {
        var start = output.IndexOf('{');
        var end = output.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            var root = JsonNode.Parse(output[start..(end + 1)])?.AsObject();
            if (root is null)
            {
                return null;
            }

            var senses = new List<DictionarySense>();
            foreach (var sense in root["senses"]?.AsArray() ?? [])
            {
                if (senses.Count >= DictionaryCard.MaxSenses)
                {
                    break;
                }

                var definitions = Strings(sense?["definitions"], DictionaryCard.MaxDefinitionsPerSense);
                if (definitions.Count == 0)
                {
                    continue;
                }

                senses.Add(new DictionarySense(
                    sense?["partOfSpeech"]?.GetValue<string>(),
                    definitions,
                    Strings(sense?["examples"], DictionaryCard.MaxExamplesPerSense),
                    Strings(sense?["synonyms"], DictionaryCard.MaxSynonymsPerSense)));
            }

            return senses.Count == 0 ? null : new DictionaryCard(word, root["phonetic"]?.GetValue<string>(), senses);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> Strings(JsonNode? node, int limit)
    {
        var values = new List<string>();
        foreach (var item in node?.AsArray() ?? [])
        {
            if (values.Count >= limit)
            {
                return values;
            }

            if (item?.GetValue<string>() is { Length: > 0 } text)
            {
                values.Add(text);
            }
        }

        return values;
    }

    /// <summary>
    /// 调用方按次查完就释放端口（见面板的换代号机制）；为这一次查询而造的
    /// 后端——带着它的 HttpClient——一并释放，而不是等 GC 收拾套接字。
    /// </summary>
    public void Dispose() => (model as IDisposable)?.Dispose();
}

/// <summary>
/// 词典化 prompt：把通用模型按成一本词典。与翻译 prompt 同族的克制——
/// 只要一张卡，不要解释、不要围栏、不要发散。
/// </summary>
public static class DictionaryPrompt
{
    public static string SystemPrompt() => """
        You are a dictionary. The user sends one word; reply with its dictionary card as JSON and nothing else.

        Shape, exactly: {"phonetic":string or null,"senses":[{"partOfSpeech":string,"definitions":[string],"examples":[string],"synonyms":[string]}]}

        - phonetic: IPA for foreign words, pinyin with tone marks for Chinese words; unknown → null.
        - partOfSpeech in Chinese (名、动、形、副、介、连、叹…).
        - definitions: the word's meanings in the word's own language, most common first, one line each.
        - examples: natural sentences using the word, in the word's own language.
        - synonyms: words interchangeable with it in the same language.
        - At most 4 senses, and per sense at most 3 definitions, 2 examples, 4 synonyms.
        - No markdown fences, no labels, no commentary, no translation outside the card.
        - If it is not a real word, reply {"phonetic":null,"senses":[]}.
        """;

    public static string UserContent(string word) => word;
}
