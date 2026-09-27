using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class TranslationEchoTests
{
    [Fact]
    public void An_identical_translation_is_an_echo()
        => Assert.True(TranslationEcho.IsEchoish("Hello there", "Hello there"));

    [Theory]
    [InlineData("Hello, world!", "Hello world")]
    [InlineData("你好，世界。", "你好世界")]
    [InlineData("«bonjour»", "bonjour")]
    public void Punctuation_and_spacing_differences_do_not_hide_an_echo(string original, string translated)
        => Assert.True(TranslationEcho.IsEchoish(original, translated));

    [Fact]
    public void Case_differences_do_not_hide_an_echo()
        => Assert.True(TranslationEcho.IsEchoish("Hello There", "hello there"));

    [Fact]
    public void An_echo_wrapped_in_quotes_is_still_an_echo()
        => Assert.True(TranslationEcho.IsEchoish("Hello there", "\"Hello there\""));

    [Theory]
    [InlineData("Hello there", "你好，很高兴认识你")]
    [InlineData("The quick brown fox", "敏捷的棕色狐狸")]
    [InlineData("Bonjour tout le monde", "Hello everyone")]
    public void A_real_translation_is_not_an_echo(string original, string translated)
        => Assert.False(TranslationEcho.IsEchoish(original, translated));

    [Fact]
    public void A_translation_into_a_different_script_is_never_an_echo()
    {
        // 文字系统变了就谈不上"把原文吐回来"——先验先行，省掉相似度计算。
        Assert.False(TranslationEcho.IsEchoish("Hello there", "ハロー"));
        Assert.False(TranslationEcho.IsEchoish("привет", "hello"));
    }

    [Fact]
    public void A_slightly_reworded_result_is_not_treated_as_an_echo()
    {
        // 换了一个词的输出是模型在干活（哪怕是干得差），不是回声。
        Assert.False(TranslationEcho.IsEchoish("hello there", "hello their"));
        Assert.False(TranslationEcho.IsEchoish("hello world", "hello big world"));
    }

    [Theory]
    [InlineData("", "你好")]
    [InlineData("Hello", "")]
    [InlineData("   ", "   ")]
    public void Empty_text_on_either_side_is_not_an_echo(string original, string translated)
        => Assert.False(TranslationEcho.IsEchoish(original, translated));

    [Fact]
    public void A_single_identical_word_counts_as_an_echo()
    {
        // 单词被原样吐回是最常见的回声形态（专有名词、缩写）。
        Assert.True(TranslationEcho.IsEchoish("OK", "OK"));
        Assert.False(TranslationEcho.IsEchoish("OK", "好的"));
    }
}
