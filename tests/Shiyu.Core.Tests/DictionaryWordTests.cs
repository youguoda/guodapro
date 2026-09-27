using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class DictionaryWordTests
{
    [Theory]
    [InlineData("hello")]
    [InlineData("Hello")]
    [InlineData("HELLO")]
    [InlineData("don't")]
    [InlineData("well-known")]
    [InlineData("café")]
    [InlineData("ok")]
    public void A_single_latin_word_qualifies(string text)
        => Assert.True(DictionaryWord.IsEnglishWord(text));

    [Theory]
    [InlineData("hello world")]
    [InlineData("hello,")]
    [InlineData("hello.")]
    [InlineData("3rd")]
    [InlineData("vs.")]
    [InlineData("-hello")]
    [InlineData("hello-")]
    [InlineData("a")]
    [InlineData("I")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("你好")]
    public void Anything_else_does_not(string text)
        => Assert.False(DictionaryWord.IsEnglishWord(text));

    [Fact]
    public void Surrounding_whitespace_is_ignored_rather_than_disqualifying()
    {
        // 划词复制几乎总带着首尾空白；它不该把一个词判成短语。
        Assert.True(DictionaryWord.IsEnglishWord("  hello\r\n"));
    }

    [Theory]
    [InlineData("你好")]
    [InlineData("打")]
    [InlineData("人工智能")]
    [InlineData("实事求是")]
    [InlineData("恍如隔世感同身受")]
    public void A_run_of_han_characters_qualifies_as_a_chinese_word(string text)
        => Assert.True(DictionaryWord.IsChineseWord(text));

    [Theory]
    [InlineData("你好。")]
    [InlineData("你 好呀")]
    [InlineData("第1个")]
    [InlineData("hello")]
    [InlineData("ni好")]
    [InlineData("")]
    [InlineData("  ")]
    public void A_chinese_word_may_not_carry_punctuation_digits_or_other_scripts(string text)
        => Assert.False(DictionaryWord.IsChineseWord(text));

    [Fact]
    public void A_han_run_longer_than_eight_characters_is_a_phrase_not_a_word()
    {
        // 词与无标点短语的边界：8 字以内按词对待，再长更可能是选多了。
        Assert.True(DictionaryWord.IsChineseWord("一二三四五六七八"));
        Assert.False(DictionaryWord.IsChineseWord("一二三四五六七八九"));
    }

    [Fact]
    public void The_lookup_key_is_trimmed_and_case_folded_for_latin()
    {
        Assert.Equal("hello", DictionaryWord.LookupKey("  Hello "));
        Assert.Equal("don't", DictionaryWord.LookupKey("Don't"));
        Assert.Equal("你好", DictionaryWord.LookupKey("你好"));
    }
}

public class SpeechLanguageTests
{
    [Theory]
    [InlineData("Chinese", "zh")]
    [InlineData("English", "en")]
    [InlineData("Japanese", "ja")]
    [InlineData("Korean", "ko")]
    [InlineData("Russian", "ru")]
    [InlineData("chinese", "zh")]
    [InlineData("  ENGLISH ", "en")]
    public void Known_language_names_map_to_their_culture_prefix(string name, string prefix)
        => Assert.Equal(prefix, SpeechLanguage.CulturePrefix(name));

    [Theory]
    [InlineData("French")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_has_no_prefix_and_will_use_the_default_voice(string? name)
        => Assert.Null(SpeechLanguage.CulturePrefix(name));
}
