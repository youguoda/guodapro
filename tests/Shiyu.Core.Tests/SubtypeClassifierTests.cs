using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// The subtype classifier answers "what does this entry look like" so the
/// list can show it more usefully — a different question from the badge
/// filter's "should this interrupt the user". The two must never merge:
/// different thresholds, different reasons to change.
/// </summary>
public class SubtypeClassifierTests
{
    [Theory]
    [InlineData("https://example.com/path?q=1")]
    [InlineData("http://localhost:8080/index.html")]
    [InlineData("  https://example.com/  ")]
    public void Whole_entry_urls_are_links(string text)
        => Assert.Equal(EntrySubtype.Link, SubtypeClassifier.Detect(text));

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("first.last@example.co")]
    [InlineData("first_last@example.com")]
    [InlineData("first+tag@example.com")]
    [InlineData("first-tag@example.com")]
    [InlineData("first%last@example.com")]
    [InlineData("first.last_+tag-%25@example.co.uk")]
    public void Emails_with_generous_local_parts_are_emails(string text)
        => Assert.Equal(EntrySubtype.Email, SubtypeClassifier.Detect(text));

    [Theory]
    [InlineData("#fff", 255, 255, 255)]
    [InlineData("#1F6FEB", 31, 111, 235)]
    [InlineData("rgb(255, 0, 170)", 255, 0, 170)]
    [InlineData("rgba(26,102,219,0.5)", 26, 102, 219)]
    public void Common_colour_notations_are_colours(string text, byte r, byte g, byte b)
    {
        Assert.Equal(EntrySubtype.Color, SubtypeClassifier.Detect(text));

        Assert.True(SubtypeColor.TryParse(text, out var colour));
        Assert.Equal(r, colour.R);
        Assert.Equal(g, colour.G);
        Assert.Equal(b, colour.B);
    }

    [Theory]
    [InlineData(@"C:\Users\someone\file.txt", EntrySubtype.LocalPath)]
    [InlineData(@"D:\projects\guodapro", EntrySubtype.LocalPath)]
    [InlineData(@"\\server\share\doc.pdf", EntrySubtype.UncPath)]
    [InlineData("/usr/local/bin/env", EntrySubtype.LocalPath)]
    public void Local_and_unc_paths_are_paths(string text, EntrySubtype expected)
        => Assert.Equal(expected, SubtypeClassifier.Detect(text));

    [Theory]
    [InlineData("今天天气不错，出去走走。")]
    [InlineData("The quick brown fox jumps over the lazy dog.")]
    [InlineData("def f(x):\n    return x + 1")]
    [InlineData("1234567890")]
    [InlineData("2026-09-25")]
    [InlineData("价格 42.50 元")]
    public void Ordinary_text_is_not_mistaken_for_anything(string text)
        => Assert.Equal(EntrySubtype.None, SubtypeClassifier.Detect(text));

    [Fact]
    public void A_url_mentioned_inside_prose_is_not_a_link_entry()
        => Assert.Equal(EntrySubtype.None,
            SubtypeClassifier.Detect("看看这个 https://example.com 很有意思"));

    [Theory]
    [InlineData("hello world")]
    [InlineData("查看 C 盘")]
    [InlineData("user@example")]
    [InlineData("#GGG")]
    public void Almost_matches_are_rejected(string text)
        => Assert.Equal(EntrySubtype.None, SubtypeClassifier.Detect(text));
}
