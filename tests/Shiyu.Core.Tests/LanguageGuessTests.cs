using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class LanguageGuessTests
{
    [Theory]
    [InlineData("今天天气不错，我们去公园走走。", TextScript.Han)]
    [InlineData("这个 bug 很难排查", TextScript.Han)]
    [InlineData("こんにちは、世界。", TextScript.Japanese)]
    [InlineData("私は東京へ行きます。", TextScript.Japanese)]
    [InlineData("안녕하세요, 반갑습니다.", TextScript.Korean)]
    [InlineData("Привет, как дела?", TextScript.Cyrillic)]
    [InlineData("Hello there, general greeting.", TextScript.Latin)]
    [InlineData("Café déjà vu naïve", TextScript.Latin)]
    public void The_dominant_script_of_a_text_is_identified(string text, TextScript expected)
        => Assert.Equal(expected, LanguageGuess.FromText(text).Script);

    [Fact]
    public void Kana_marks_a_text_as_Japanese_even_when_kanji_dominate()
    {
        // 假名只出现在日文里：汉字再多，一个假名就说明这不是中文。
        Assert.Equal(TextScript.Japanese, LanguageGuess.FromText("東京都庁に行かなければならない。").Script);
    }

    [Theory]
    [InlineData("Please translate this phrase for me, it has one 汉字 word", TextScript.Latin)]
    [InlineData("今天下午三点的 meeting 改到五点了", TextScript.Han)]
    public void Mixed_text_follows_the_majority(string text, TextScript expected)
        => Assert.Equal(expected, LanguageGuess.FromText(text).Script);

    [Theory]
    [InlineData("123 456")]
    [InlineData("!!! ???")]
    [InlineData("")]
    public void Text_without_letters_of_any_known_script_is_unknown(string text)
        => Assert.Equal(TextScript.Unknown, LanguageGuess.FromText(text).Script);

    [Fact]
    public void A_guessed_script_carries_both_a_label_and_a_prompt_language_name()
    {
        // 两个消费方向：面板方向标签要中文短名，换向重试要能直接进 prompt 的英文名。
        var guess = LanguageGuess.FromText("你好世界");
        Assert.Equal("中文", guess.Label);
        Assert.Equal("Chinese", guess.LanguageName);

        Assert.Equal("英文", LanguageGuess.FromText("hello").Label);
        Assert.Equal("English", LanguageGuess.FromText("hello").LanguageName);
        Assert.Equal("俄文", LanguageGuess.FromText("привет").Label);
        Assert.Equal("Russian", LanguageGuess.FromText("привет").LanguageName);
        Assert.Equal("日文", LanguageGuess.FromText("こんにちは").Label);
        Assert.Equal("Japanese", LanguageGuess.FromText("こんにちは").LanguageName);
        Assert.Equal("韩文", LanguageGuess.FromText("안녕").Label);
        Assert.Equal("Korean", LanguageGuess.FromText("안녕").LanguageName);
    }

    [Fact]
    public void An_unknown_script_guesses_nothing()
    {
        var guess = LanguageGuess.FromText("42");
        Assert.Null(guess.Label);
        Assert.Null(guess.LanguageName);
    }
}
