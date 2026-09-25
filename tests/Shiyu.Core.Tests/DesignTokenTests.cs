using Shiyu.Core;

namespace Shiyu.Core.Tests;

/// <summary>
/// The design tokens are data with product requirements attached: the same
/// slot set in every theme, readable text on every surface the interface
/// actually draws, line heights loose enough for Chinese. Those requirements
/// are cheaper to enforce here, once, than to re-check by eye in five windows.
/// </summary>
public class DesignTokenTests
{
    public static TheoryData<string> Palettes => new() { "Light", "Dark" };

    private static ThemePalette Palette(string name)
        => name == "Light" ? DesignTokens.Light : DesignTokens.Dark;

    [Theory]
    [MemberData(nameof(Palettes))]
    public void A_theme_defines_exactly_the_agreed_slots(string theme)
    {
        var palette = Palette(theme);

        // A slot present in one theme and missing in the other is a window
        // that cannot follow the theme switch — so the sets must match exactly,
        // not merely overlap.
        Assert.Equal(
            DesignTokens.Slots.OrderBy(slot => slot),
            palette.Colors.Keys.OrderBy(slot => slot));
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Every_token_colour_is_an_argb_hex_literal(string theme)
    {
        foreach (var (_, hex) in Palette(theme).Colors)
        {
            Assert.True(hex.StartsWith('#') && hex.Length == 9,
                $"{theme}: 「{hex}」不是 #AARRGGBB 形式");
            Assert.True(uint.TryParse(hex[1..], System.Globalization.NumberStyles.HexNumber, null, out _),
                $"{theme}: 「{hex}」解析不了");
        }
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Text_stays_readable_on_every_surface_it_is_drawn_on(string theme)
    {
        var palette = Palette(theme);

        foreach (var (foreground, background) in DesignTokens.ReadablePairs)
        {
            var ratio = Contrast(palette.Colors[foreground], palette.Colors[background]);

            // 4.5 is the WCAG AA bar for normal text, and everything these
            // windows draw is normal-sized or smaller.
            Assert.True(ratio >= 4.5,
                $"{theme}: {foreground} 在 {background} 上对比度 {ratio:0.00}，不足 4.5");
        }
    }

    [Fact]
    public void Readable_pairs_only_reference_real_slots()
    {
        foreach (var (foreground, background) in DesignTokens.ReadablePairs)
        {
            Assert.Contains(foreground, DesignTokens.Slots);
            Assert.Contains(background, DesignTokens.Slots);
        }
    }

    [Fact]
    public void Chinese_body_line_height_is_looser_than_the_english_default()
    {
        // Chinese has no inter-word spaces: at the English-typical 1.5 a whole
        // paragraph reads as one block. 1.7 is the floor this app commits to.
        Assert.True(DesignTokens.BodyLineRatio >= 1.7,
            $"正文行高比例 {DesignTokens.BodyLineRatio} 低于 1.7");
    }

    [Fact]
    public void Line_heights_are_derived_from_the_ratio_not_hand_picked()
    {
        foreach (var size in new[] { DesignTokens.FontSecondary, DesignTokens.FontBody, DesignTokens.FontBodyLarge })
        {
            var line = DesignTokens.LineHeightFor(size);

            // Rounding to whole pixels may shave a little, never more than
            // half a line ratio's worth.
            Assert.True(line >= size * (DesignTokens.BodyLineRatio - 0.05),
                $"字号 {size} 的行高 {line} 不是按比例 {DesignTokens.BodyLineRatio} 推出的");
        }
    }

    [Fact]
    public void Both_font_chains_name_a_cjk_capable_fallback()
    {
        // Without a CJK font in the chain, mixed Chinese/Latin text jumps
        // baseline as WPF links glyph by glyph; with one it does not.
        Assert.Contains("YaHei", DesignTokens.FamilyUi, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("YaHei", DesignTokens.FamilyMono, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_mono_chain_starts_with_an_actual_monospace_font()
    {
        Assert.Contains("Consolas", DesignTokens.FamilyMono, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_spacing_scale_is_strictly_ascending_and_distinct()
    {
        Assert.Equal(DesignTokens.SpacingScale.Distinct(), DesignTokens.SpacingScale);
        Assert.Equal(DesignTokens.SpacingScale.OrderBy(v => v), DesignTokens.SpacingScale);
    }

    [Fact]
    public void A_fresh_install_follows_whatever_the_system_prefers()
    {
        Assert.Equal(AppTheme.System, new AppSettings().Theme);
    }

    private static (double Alpha, double R, double G, double B) Argb(string hex)
    {
        var value = uint.Parse(hex[1..], System.Globalization.NumberStyles.HexNumber);
        return ((value >> 24) & 255, (value >> 16) & 255, (value >> 8) & 255, value & 255);
    }

    private static double Contrast(string foreground, string background)
    {
        var (alpha, fr, fg, fb) = Argb(foreground);
        var (_, br, bg, bb) = Argb(background);

        // Semi-transparent ink is always composited over something; the pairs
        // name what it is drawn on, so blend before measuring.
        var share = alpha / 255.0;
        var r = fr * share + br * (1 - share);
        var g = fg * share + bg * (1 - share);
        var b = fb * share + bb * (1 - share);

        var first = Luminance(r, g, b);
        var second = Luminance(br, bg, bb);

        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static double Luminance(double r, double g, double b)
        => 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);

    private static double Channel(double component)
    {
        var normalised = component / 255.0;

        return normalised <= 0.04045
            ? normalised / 12.92
            : Math.Pow((normalised + 0.055) / 1.055, 2.4);
    }
}
