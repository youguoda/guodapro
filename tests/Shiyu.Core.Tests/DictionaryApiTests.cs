using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class FreeDictionaryApiTests
{
    /// <summary>dictionaryapi.dev 的真实应答形状（“hello” 词条节选）。</summary>
    private const string HelloPayload = """
        [
          {
            "word": "hello",
            "phonetic": "həˈləʊ",
            "phonetics": [
              {"text": "həˈləʊ", "audio": "hello--_gb_1.mp3"},
              {"text": "hɛˈləʊ"}
            ],
            "meanings": [
              {
                "partOfSpeech": "exclamation",
                "definitions": [
                  {"definition": "used as a greeting or to begin a phone conversation.", "example": "hello there, Katie!"},
                  {"definition": "used to express surprise.", "example": null, "synonyms": ["hallo"]}
                ],
                "synonyms": ["hallo"]
              },
              {
                "partOfSpeech": "noun",
                "definitions": [
                  {"definition": "an utterance of “hello”; a greeting."}
                ],
                "synonyms": []
              }
            ]
          }
        ]
        """;

    private static FreeDictionaryApi Api(ScriptedHandler handler) => new(new HttpClient(handler));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task A_successful_lookup_becomes_a_card()
    {
        var handler = new ScriptedHandler(_ => Json(HelloPayload));
        var card = await Api(handler).LookupAsync("Hello");

        Assert.NotNull(card);
        Assert.Equal("hello", card.Word);
        Assert.Equal("həˈləʊ", card.Phonetic);

        var first = Assert.Single(card.Senses, s => s.PartOfSpeech == "exclamation");
        Assert.Equal(
            "used as a greeting or to begin a phone conversation.",
            first.Definitions[0]);
        Assert.Equal("hello there, Katie!", Assert.Single(first.Examples));
        Assert.Contains("hallo", first.Synonyms);
    }

    [Fact]
    public async Task The_request_targets_the_documented_entries_path()
    {
        var handler = new ScriptedHandler(_ => Json(HelloPayload));
        await Api(handler).LookupAsync("Hello");

        var request = Assert.Single(handler.Requests).Message;
        Assert.Equal(
            "https://api.dictionaryapi.dev/api/v2/entries/en/hello",
            request.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Fact]
    public async Task A_missing_top_level_phonetic_falls_back_to_the_phonetics_array()
    {
        var payload = """
            [{"word":"world","phonetics":[{"audio":"x.mp3"},{"text":"wɜːld"}],
              "meanings":[{"partOfSpeech":"noun","definitions":[{"definition":"the earth."}]}]}]
            """;
        var handler = new ScriptedHandler(_ => Json(payload));

        var card = await Api(handler).LookupAsync("world");

        Assert.NotNull(card);
        Assert.Equal("wɜːld", card.Phonetic);
    }

    [Fact]
    public async Task An_unknown_word_is_not_found_and_yields_no_card()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Null(await Api(handler).LookupAsync("notaword"));
    }

    [Fact]
    public async Task A_broken_payload_yields_no_card_rather_than_a_throw()
    {
        // 静默放弃是端口的契约：坏 JSON 对外只是“没有卡”。
        var handler = new ScriptedHandler(_ => Json("{not json at all"));
        Assert.Null(await Api(handler).LookupAsync("hello"));
    }

    [Fact]
    public async Task A_network_failure_yields_no_card_rather_than_a_throw()
    {
        var handler = new ScriptedHandler(_ => throw new HttpRequestException("offline"));
        Assert.Null(await Api(handler).LookupAsync("hello"));
    }

    [Fact]
    public async Task The_card_is_bounded_to_stay_compact()
    {
        // 紧凑卡：最多 4 义项、每义 3 条释义、2 条例句、4 个同义词——
        // 词条页是词典网站的事，面板要的是一屏读完。
        var payload = """
            [{"word":"set","meanings": [
              {"partOfSpeech":"verb","definitions":[
                {"definition":"d1"},{"definition":"d2"},{"definition":"d3"},{"definition":"d4"},
                {"definition":"d5"}],
               "synonyms":["s1","s2","s3","s4","s5"]},
              {"partOfSpeech":"noun","definitions":[{"definition":"n1"}]},
              {"partOfSpeech":"adj","definitions":[{"definition":"a1"}]},
              {"partOfSpeech":"adverb","definitions":[{"definition":"adv1"}]},
              {"partOfSpeech":"preposition","definitions":[{"definition":"prep1"}]}
            ]}]
            """;
        var handler = new ScriptedHandler(_ => Json(payload));

        var card = await Api(handler).LookupAsync("set");

        Assert.NotNull(card);
        Assert.Equal(4, card.Senses.Count);
        var verb = card.Senses[0];
        Assert.Equal(3, verb.Definitions.Count);
        Assert.Equal(4, verb.Synonyms.Count);
    }

    [Fact]
    public async Task A_payload_without_any_usable_sense_yields_no_card()
    {
        var handler = new ScriptedHandler(_ => Json("""[{"word":"x","meanings":[]}]"""));
        Assert.Null(await Api(handler).LookupAsync("x"));
    }
}

public class LlmDictionaryApiTests
{
    /// <summary>按剧本分片吐出应答的假模型——词典 JSON 也要经得起流式切片。</summary>
    private sealed class ScriptedModel(params string[] pieces) : IStreamingModel
    {
        public List<ModelRequest> Requests { get; } = [];

        public Exception? ThrowInstead { get; init; }

        public async IAsyncEnumerable<string> StreamAsync(
            ModelRequest request,
            [EnumeratorCancellation] CancellationToken cancellation)
        {
            Requests.Add(request);
            if (ThrowInstead is not null)
            {
                throw ThrowInstead;
            }

            foreach (var piece in pieces)
            {
                yield return piece;
                await Task.Yield();
            }
        }
    }

    private const string CardJson =
        """{"phonetic":"nǐ hǎo","senses":[{"partOfSpeech":"叹","definitions":["打招呼用语。"],"examples":["你好，请问车站怎么走？"],"synonyms":["您好","你们好"]}]}""";

    [Fact]
    public async Task A_fenced_streamed_card_json_becomes_a_card()
    {
        // 模型再被叮嘱也爱加 markdown 围栏——解析要扛得住。
        var model = new ScriptedModel("```json\n", "{\"phonetic\":\"nǐ hǎo\",", "\n\"senses\":[", "{\"partOfSpeech\":\"叹\",\"definitions\":[\"打招呼用语。\"],", "\"examples\":[\"你好，请问车站怎么走？\"],\"synonyms\":[\"您好\",\"你们好\"]}]}", "\n```");
        var api = new LlmDictionaryApi(model);

        var card = await api.LookupAsync("你好");

        Assert.NotNull(card);
        Assert.Equal("你好", card.Word);
        Assert.Equal("nǐ hǎo", card.Phonetic);
        var sense = Assert.Single(card.Senses);
        Assert.Equal("叹", sense.PartOfSpeech);
        Assert.Equal("打招呼用语。", Assert.Single(sense.Definitions));
        Assert.Equal("您好", sense.Synonyms[0]);
    }

    [Fact]
    public async Task The_prompt_asks_for_a_dictionary_card_and_sends_only_the_word()
    {
        var model = new ScriptedModel(CardJson);
        await new LlmDictionaryApi(model).LookupAsync("你好");

        var request = Assert.Single(model.Requests);
        Assert.Contains("dictionary", request.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JSON", request.SystemPrompt);
        Assert.Equal("你好", request.UserContent);

        // 词典卡要的是确定的事实，不是有创意的一个：沿用翻译的低温。
        Assert.Equal(TranslationPrompt.DefaultTemperature, request.Temperature);
    }

    [Fact]
    public async Task Gibberish_yields_no_card_rather_than_a_throw()
    {
        var model = new ScriptedModel("你好就是 hello 的意思");
        Assert.Null(await new LlmDictionaryApi(model).LookupAsync("你好"));
    }

    [Fact]
    public async Task A_not_a_word_reply_yields_no_card()
    {
        var model = new ScriptedModel("""{"phonetic":null,"senses":[]}""");
        Assert.Null(await new LlmDictionaryApi(model).LookupAsync("qqq"));
    }

    [Fact]
    public async Task A_model_failure_yields_no_card_rather_than_a_throw()
    {
        var model = new ScriptedModel("never streamed")
        {
            ThrowInstead = new TranslationFailedException("服务不可用"),
        };

        Assert.Null(await new LlmDictionaryApi(model).LookupAsync("你好"));
    }
}

public class BudgetedDictionaryTests
{
    /// <summary>永不主动完成、只在被取消时醒来的假词典——钉住预算外壳的行为。</summary>
    private sealed class HangingDictionary : IDictionaryApi
    {
        private readonly TaskCompletionSource _cancelled = new();

        public Task Cancelled => _cancelled.Task;

        public async Task<DictionaryCard?> LookupAsync(
            string word, CancellationToken cancellation = default)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellation);
            }
            catch (OperationCanceledException)
            {
                _cancelled.SetResult();
            }

            return null;
        }
    }

    private sealed class CardDictionary : IDictionaryApi
    {
        public Task<DictionaryCard?> LookupAsync(
            string word, CancellationToken cancellation = default)
            => Task.FromResult<DictionaryCard?>(
                new DictionaryCard(word, null, [new DictionarySense("名", ["释义"], [], [])]));
    }

    private sealed class ThrowingDictionary : IDictionaryApi
    {
        public Task<DictionaryCard?> LookupAsync(
            string word, CancellationToken cancellation = default)
            => throw new HttpRequestException("offline");
    }

    [Fact]
    public void The_default_budget_is_the_six_hundred_milliseconds_the_ticket_names()
        => Assert.Equal(TimeSpan.FromMilliseconds(600), BudgetedDictionary.DefaultBudget);

    [Fact]
    public async Task A_lookup_past_its_budget_is_abandoned_silently()
    {
        var hanging = new HangingDictionary();
        var api = new BudgetedDictionary(hanging, TimeSpan.FromMilliseconds(80));

        // 超预算的返回是 null——没有卡，没有异常，没有等待。
        Assert.Null(await api.LookupAsync("hello"));

        // 内层确实收到了取消，而不是被晾在后台。
        await hanging.Cancelled.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_lookup_inside_its_budget_passes_through()
    {
        var api = new BudgetedDictionary(new CardDictionary(), TimeSpan.FromSeconds(5));

        var card = await api.LookupAsync("hello");

        Assert.NotNull(card);
        Assert.Equal("hello", card.Word);
    }

    [Fact]
    public async Task A_throwing_inner_is_silenced_into_no_card()
        => Assert.Null(await new BudgetedDictionary(new ThrowingDictionary())
            .LookupAsync("hello"));
}
