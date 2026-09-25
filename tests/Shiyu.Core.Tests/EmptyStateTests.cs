using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// The empty list has to say why it is empty — exactly which conditions ate
/// everything — and never leak a placeholder or claim a way out that is not
/// there. Every combination of the four filter axes gets its own words.
/// </summary>
public class EmptyStateTests
{
    public static TheoryData<bool, bool, bool, bool> Combinations = new()
    {
        { false, false, false, false },
        { true, false, false, false },
        { false, true, false, false },
        { false, false, true, false },
        { false, false, false, true },
        { true, true, false, false },
        { true, false, true, false },
        { true, false, false, true },
        { false, true, true, false },
        { false, true, false, true },
        { false, false, true, true },
        { true, true, true, false },
        { true, true, false, true },
        { true, false, true, true },
        { false, true, true, true },
        { true, true, true, true },
    };

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Every_combination_names_its_conditions_and_offers_a_way_out(
        bool withGroup, bool withQuery, bool withKind, bool favouriteOnly)
    {
        var copy = EmptyStates.For(
            new HistoryFilter
            {
                Query = withQuery ? "发票" : null,
                Kind = withKind ? EntryKind.Image : null,
                Favorite = favouriteOnly ? true : null,
                Group = withGroup ? 7 : null,
            },
            withGroup ? "工作" : null,
            totalEntries: 30);

        if (withGroup)
        {
            Assert.Contains("分组「工作」", copy.Headline);
            Assert.Contains("「工作」", copy.Hint);
        }

        if (withQuery)
        {
            Assert.Contains("包含「发票」", copy.Headline);
        }

        if (withKind)
        {
            Assert.Contains("图片", copy.Headline);
        }

        if (favouriteOnly)
        {
            Assert.Contains("收藏的", copy.Headline);
        }

        // The whole sentence, not fragments left for the reader to assemble.
        Assert.Contains("还没有", copy.Headline);
        Assert.DoesNotContain("{", copy.Headline + copy.Hint);
        Assert.True(copy.OfferClear);
        Assert.NotEqual("", copy.Hint.Trim());
    }

    [Fact]
    public void A_history_with_nothing_in_it_points_at_copying_not_at_filters()
    {
        var copy = EmptyStates.For(
            new HistoryFilter { Query = "whatever", Favorite = true },
            groupName: "工作",
            totalEntries: 0);

        Assert.Equal("还没有任何记录", copy.Headline);
        Assert.Contains("复制", copy.Hint);

        // Clearing filters can never help here, so it is not offered.
        Assert.False(copy.OfferClear);
    }

    [Fact]
    public void Four_conditions_at_once_read_as_one_sentence()
    {
        var copy = EmptyStates.For(
            new HistoryFilter { Query = "发票", Kind = EntryKind.Files, Favorite = true, Group = 3 },
            "工作",
            totalEntries: 5);

        Assert.Equal("分组「工作」里还没有包含「发票」的收藏的文件内容", copy.Headline);
    }

    [Fact]
    public void Long_input_does_not_leave_placeholders_or_blanks()
    {
        var longWord = new string('长', 40);
        var copy = EmptyStates.For(
            new HistoryFilter { Query = longWord },
            "超" + new string('长', 40),
            totalEntries: 2);

        Assert.Contains(longWord, copy.Headline);
        Assert.DoesNotContain("{", copy.Headline + copy.Hint);
    }

    [Fact]
    public void An_unfiltered_non_empty_history_falls_back_to_honest_generic_words()
    {
        var copy = EmptyStates.For(HistoryFilter.None, groupName: null, totalEntries: 9);

        Assert.Contains("还没有", copy.Headline);
        Assert.True(copy.OfferClear);
    }
}
