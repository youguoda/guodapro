using System.Text.Json.Nodes;

namespace Shiyu.Core;

/// <summary>
/// dictionaryapi.dev 的适配器（免费、无 key、仅英文单词）。
///
/// 两阶段词典的快路：翻译先出，这张卡在 600ms 预算内补上（预算由
/// <see cref="BudgetedDictionary"/> 执行）。它自己不做退避重试——预算
/// 装不下一次重试，静默放弃比磨蹭到卡顿体面。
/// </summary>
public sealed class FreeDictionaryApi : IDictionaryApi, IDisposable
{
    private const string Endpoint = "https://api.dictionaryapi.dev/api/v2/entries/en/";

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public FreeDictionaryApi(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _ownsClient = http is null;
    }

    public async Task<DictionaryCard?> LookupAsync(string word, CancellationToken cancellation = default)
    {
        try
        {
            using var response = await _http.GetAsync(
                Endpoint + Uri.EscapeDataString(DictionaryWord.LookupKey(word)),
                cancellation);

            if (!response.IsSuccessStatusCode)
            {
                // 404 是“词典里没有这个词”，与其它失败同样是“没有卡”。
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellation);
            return Parse(json, word);
        }
        catch (Exception failure)
        {
            // 网络、超时、坏 JSON：静默放弃是端口的契约。404（无此词）不走
            // 这里、也不留痕；留下的是基础设施类失败——正是 O-24 要的
            // "回退链为何总是走慢路"的那条证据（2026-09-27 实测不可达）。
            Log.Event(LogEvent.DictionaryLookupFailed, failure, ("leg", 0));
            return null;
        }
    }

    /// <summary>dictionaryapi.dev 的应答是词条数组；形状对不上就放弃。</summary>
    private static DictionaryCard? Parse(string json, string asked)
    {
        try
        {
            var entry = (JsonNode.Parse(json) as JsonArray)?[0]?.AsObject();
            if (entry is null)
            {
                return null;
            }

            var word = entry["word"]?.GetValue<string>() is { Length: > 0 } spelled
                ? spelled
                : asked;

            var senses = ParseSenses(entry["meanings"]?.AsArray());
            if (senses.Count == 0)
            {
                return null;
            }

            return new DictionaryCard(word, ParsePhonetic(entry), senses);
        }
        catch (Exception)
        {
            // expected: JsonException/InvalidOperationException 都只是"这张
            // 卡拼不出来"。
            return null;
        }
    }

    private static string? ParsePhonetic(JsonObject entry)
    {
        if (entry["phonetic"]?.GetValue<string>() is { Length: > 0 } direct)
        {
            return direct;
        }

        var phonetics = entry["phonetics"]?.AsArray();
        if (phonetics is null)
        {
            return null;
        }

        foreach (var slot in phonetics)
        {
            if (slot?["text"]?.GetValue<string>() is { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }

    private static IReadOnlyList<DictionarySense> ParseSenses(JsonArray? meanings)
    {
        var senses = new List<DictionarySense>();

        foreach (var meaning in meanings ?? [])
        {
            if (senses.Count >= DictionaryCard.MaxSenses)
            {
                break;
            }

            var definitions = new List<string>();
            var examples = new List<string>();
            var synonyms = new List<string>();

            var partOfSpeech = meaning?["partOfSpeech"]?.GetValue<string>();

            foreach (var definition in meaning?["definitions"]?.AsArray() ?? [])
            {
                if (definitions.Count >= DictionaryCard.MaxDefinitionsPerSense
                    && examples.Count >= DictionaryCard.MaxExamplesPerSense)
                {
                    break;
                }

                if (definition?["definition"]?.GetValue<string>() is { Length: > 0 } text
                    && definitions.Count < DictionaryCard.MaxDefinitionsPerSense)
                {
                    definitions.Add(text);
                }

                if (definition?["example"]?.GetValue<string>() is { Length: > 0 } example
                    && examples.Count < DictionaryCard.MaxExamplesPerSense)
                {
                    examples.Add(example);
                }

                Collect(definition?["synonyms"]?.AsArray(), synonyms, DictionaryCard.MaxSynonymsPerSense);
            }

            Collect(meaning?["synonyms"]?.AsArray(), synonyms, DictionaryCard.MaxSynonymsPerSense);

            if (definitions.Count > 0)
            {
                senses.Add(new DictionarySense(
                    partOfSpeech, definitions, examples,
                    synonyms.Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
            }
        }

        return senses;
    }

    private static void Collect(JsonArray? source, List<string> into, int limit)
    {
        foreach (var item in source ?? [])
        {
            if (into.Count >= limit)
            {
                return;
            }

            if (item?.GetValue<string>() is { Length: > 0 } word
                && !into.Contains(word, StringComparer.OrdinalIgnoreCase))
            {
                into.Add(word);
            }
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
