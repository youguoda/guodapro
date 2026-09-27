using Shiyu.Core;

namespace Shiyu.Core.Tests;

public class SentenceAlignTests
{
    [Fact]
    public void Equal_sentence_counts_pair_one_to_one()
    {
        var pairs = SentenceAlign.Pair("One. Two. Three.", "一。二。三。");

        Assert.Equal(
        [
            new SentencePair("One.", "一。"),
            new SentencePair("Two.", "二。"),
            new SentencePair("Three.", "三。"),
        ], pairs);
    }

    [Fact]
    public void English_enders_split_too()
    {
        var pairs = SentenceAlign.Pair("Hello! How are you? Fine.", "你好！你好吗？很好。");

        Assert.Equal(3, pairs.Count);
        Assert.Equal(new SentencePair("Hello!", "你好！"), pairs[0]);
        Assert.Equal(new SentencePair("How are you?", "你好吗？"), pairs[1]);
    }

    [Fact]
    public void Text_without_enders_is_one_whole_pair()
    {
        // 无句末标点的短文本（单词、短语）不该被硬拆——一对整体最诚实。
        var pairs = SentenceAlign.Pair("hello world", "你好，世界");

        Assert.Equal([new SentencePair("hello world", "你好，世界")], pairs);
    }

    [Theory]
    [InlineData("", "你好。")]
    [InlineData("Hello.", "")]
    [InlineData("   ", "你好。")]
    public void An_empty_side_means_no_pairs(string original, string translated)
        => Assert.Empty(SentenceAlign.Pair(original, translated));

    [Fact]
    public void More_originals_than_translations_merge_the_originals()
    {
        // 原文四句、译文两句（译文一句顶两句）：按比例把多的一侧并拢，
        // 数量向少的一侧看齐——Glossy merge_meanings 的思路。
        var pairs = SentenceAlign.Pair("一。二。三。四。", "One and two. Three and four.");

        Assert.Equal(
        [
            new SentencePair("一。二。", "One and two."),
            new SentencePair("三。四。", "Three and four."),
        ], pairs);
    }

    [Fact]
    public void A_non_integer_ratio_splits_proportionally()
    {
        // 3:2 → 边界是 [0,1) 与 [1,3)：第一句单独配第一译，后两句并配第二译。
        var pairs = SentenceAlign.Pair("A. B. C.", "X. Y.");

        Assert.Equal(
        [
            new SentencePair("A.", "X."),
            new SentencePair("B. C.", "Y."),
        ], pairs);
    }

    [Fact]
    public void More_translations_than_originals_merge_the_translations()
    {
        // 反向不齐同样成立：译文多出的句子按比例并回原文的句数。
        var pairs = SentenceAlign.Pair("Hello there.", "一。二。");

        Assert.Equal([new SentencePair("Hello there.", "一。二。")], pairs);
    }

    [Fact]
    public void Merged_latin_fragments_rejoin_with_a_space()
    {
        // 英文片段以句号结尾，直接拼接会变成 "One.Two."——拉丁侧要补空格；
        // CJK 侧自带全角标点，拼接不加空格（上面各例已钉住）。
        var pairs = SentenceAlign.Pair("A. B.", "One and two.");

        Assert.Equal([new SentencePair("A. B.", "One and two.")], pairs);
    }

    [Fact]
    public void A_decimal_point_is_not_a_sentence_boundary()
    {
        var pairs = SentenceAlign.Pair("Pi is 3.14 exactly.", "圆周率约是 3.14。");

        Assert.Equal([new SentencePair("Pi is 3.14 exactly.", "圆周率约是 3.14。")], pairs);
    }

    [Fact]
    public void Trailing_text_without_an_ender_is_its_own_segment()
    {
        var pairs = SentenceAlign.Pair("Hello. World", "你好。世界");

        Assert.Equal(
        [
            new SentencePair("Hello.", "你好。"),
            new SentencePair("World", "世界"),
        ], pairs);
    }

    [Fact]
    public void Consecutive_enders_and_trailing_quotes_stay_with_their_sentence()
    {
        // "?!" 是一个句子的收尾，不该拆成两段；句末引号跟着句子走
        // （英文排版习惯：句号在引号之内收句）。
        var pairs = SentenceAlign.Pair("What?! He said \"Hi!\" and left.", "什么？！他说“嗨”就走了。");

        Assert.Equal(
        [
            new SentencePair("What?!", "什么？！"),
            new SentencePair("He said \"Hi!\" and left.", "他说“嗨”就走了。"),
        ], pairs);
    }

    [Fact]
    public void Line_breaks_are_boundaries_too()
    {
        // 列表式内容（prompt 明文保留换行）靠换行分行，没有句末标点也对得上。
        var pairs = SentenceAlign.Pair("One\nTwo", "一\n二");

        Assert.Equal(
        [
            new SentencePair("One", "一"),
            new SentencePair("Two", "二"),
        ], pairs);
    }

    [Fact]
    public void A_one_sided_punctuation_style_still_aligns_by_merging()
    {
        // 一侧用句号、另一侧全用换行：各自切完再按比例并拢，仍是一对一。
        var pairs = SentenceAlign.Pair("One. Two.", "一\n二");

        Assert.Equal(
        [
            new SentencePair("One.", "一"),
            new SentencePair("Two.", "二"),
        ], pairs);
    }
}
