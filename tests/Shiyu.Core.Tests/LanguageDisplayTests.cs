using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>票 22 头部：语言名统一中文显示。设置值与先验短名同表。</summary>
public class LanguageDisplayTests
{
    [Theory]
    [InlineData("English", "英语")]
    [InlineData("english", "英语")]
    [InlineData("英文", "英语")]
    [InlineData("Chinese", "中文")]
    [InlineData("mandarin", "中文")]
    [InlineData("Japanese", "日语")]
    [InlineData("日文", "日语")]
    [InlineData("Korean", "韩语")]
    [InlineData("韩文", "韩语")]
    [InlineData("Russian", "俄语")]
    [InlineData("俄文", "俄语")]
    [InlineData("French", "法语")]
    [InlineData("German", "德语")]
    [InlineData("Spanish", "西班牙语")]
    public void Known_language_values_map_to_chinese_names(string value, string expected)
        => Assert.Equal(expected, LanguageDisplay.Name(value));

    [Theory]
    [InlineData("Italian")]
    [InlineData("自动识别")]
    [InlineData(" 自动识别 ")]
    public void Unknown_values_show_as_they_are(string value)
        => Assert.Equal(value.Trim(), LanguageDisplay.Name(value));

    [Fact]
    public void Empty_values_stay_empty()
        => Assert.Equal(string.Empty, LanguageDisplay.Name(null));
}
