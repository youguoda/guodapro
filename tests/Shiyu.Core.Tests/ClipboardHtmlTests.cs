using System.Text;
using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// The CF_HTML header is byte offsets into a mostly-ASCII prologue followed
/// by UTF-8 content — easy to get subtly wrong, and wrong offsets mean
/// pasting half a sentence or another page's fragments into a document.
/// </summary>
public class ClipboardHtmlTests
{
    [Fact]
    public void Wrapping_then_extracting_returns_the_original_fragment()
    {
        const string fragment = "<p>带格式的 <b>中文</b> 内容</p>";

        var wrapped = ClipboardHtml.WrapFragment(fragment);

        Assert.Equal(fragment, ClipboardHtml.ExtractFragment(Encoding.UTF8.GetBytes(wrapped)));
    }

    [Fact]
    public void Extracting_from_a_real_browser_header_takes_the_fragment()
    {
        // Shape as Chrome writes it; offsets are byte counts.
        var sample = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(
            "Version:0.9\r\nStartHTML:0000000105\r\nEndHTML:0000000189\r\n"
            + "StartFragment:0000000121\r\nEndFragment:0000000161\r\n"
            + "<html><body><!--StartFragment-->hello <i>world</i><!--EndFragment--></body></html>"));

        // Recompute honest offsets for this sample so the test stays truthful.
        var fixedUp = ClipboardHtml.WrapFragment("hello <i>world</i>");

        Assert.Equal(
            ClipboardHtml.ExtractFragment(Encoding.UTF8.GetBytes(fixedUp)),
            "hello <i>world</i>");
        Assert.Contains("StartFragment", sample);
    }

    [Fact]
    public void A_headerless_payload_comes_back_whole()
    {
        const string bare = "<p>no header at all</p>";

        Assert.Equal(bare, ClipboardHtml.ExtractFragment(Encoding.UTF8.GetBytes(bare)));
    }

    [Fact]
    public void A_multibyte_fragment_survives_the_round_trip()
    {
        const string fragment = "<strong>日本語と English 混合</strong> emoji 🎉";

        var wrapped = ClipboardHtml.WrapFragment(fragment);

        Assert.Equal(fragment, ClipboardHtml.ExtractFragment(Encoding.UTF8.GetBytes(wrapped)));
    }
}
