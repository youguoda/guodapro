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

    /// <summary>票 19：非文本对比（WCAG 1.4.11）——识别控件的描边与焦点环 ≥ 3:1。</summary>
    [Theory]
    [MemberData(nameof(Palettes))]
    public void Control_strokes_and_focus_rings_hold_non_text_contrast(string theme)
    {
        var palette = Palette(theme);

        foreach (var (stroke, background) in DesignTokens.NonTextPairs)
        {
            var ratio = Contrast(palette.Colors[stroke], palette.Colors[background]);

            Assert.True(ratio >= 3.0,
                $"{theme}: {stroke} 在 {background} 上对比度 {ratio:0.00}，不足 3.0（非文本）");
        }
    }

    /// <summary>
    /// 票 19：叠层槽的 alpha 必须小于 FF——它们的意义就是半透明地压在材质上，
    /// 一个不小心写死成实色，材质就又被盖死了（R7 的教训）。
    /// </summary>
    [Theory]
    [MemberData(nameof(Palettes))]
    public void Overlay_slots_stay_translucent(string theme)
    {
        var palette = Palette(theme);

        foreach (var slot in DesignTokens.OverlaySlots)
        {
            var (alpha, _, _, _) = Argb(palette.Colors[slot]);

            Assert.True(alpha < 255,
                $"{theme}: {slot} 的 alpha 是 {alpha}——叠层槽不允许实色");
        }
    }

    /// <summary>
    /// 票 19：材质令牌的透明度承诺（ADR-0012 / UI 报告 §4.5）——浅 ≤ 0xD9、
    /// 深 ≤ 0xE0。写回 0xF0 就是把 Mica 又盖回"名存实亡"。
    /// </summary>
    [Theory]
    [MemberData(nameof(Palettes))]
    public void The_material_tint_stays_light_enough_to_show_the_material(string theme)
    {
        var palette = Palette(theme);
        var (alpha, _, _, _) = Argb(palette.Colors[DesignTokens.SurfaceMaterial]);
        var ceiling = theme == "Light" ? 0xD9 : 0xE0;

        Assert.True(alpha <= ceiling,
            $"{theme}: SurfaceMaterial alpha {(int)alpha:X2} 超过 {ceiling:X2}，材质会被盖住");
    }

    /// <summary>票 19 字阶 v2：档位间距足够分层（相邻档不差 1px），内容档保持中文行高。</summary>
    [Fact]
    public void The_v2_type_ladder_is_discernible_and_chinese_loose()
    {
        Assert.Equal(12, DesignTokens.TypeCaption);
        Assert.Equal(14, DesignTokens.TypeBody);
        Assert.Equal(18, DesignTokens.TypeContent);
        Assert.True(DesignTokens.LineForContent >= DesignTokens.TypeContent * DesignTokens.BodyLineRatio - 0.05,
            $"内容行高 {DesignTokens.LineForContent} 背离 1.7 比例");
        // 控件档与元信息档差 2，内容档与控件档差 4——相邻档都能看出来。
        Assert.True(DesignTokens.TypeBody - DesignTokens.TypeCaption >= 2);
        Assert.True(DesignTokens.TypeContent - DesignTokens.TypeBody >= 4);
    }

    /// <summary>票 19 语义间距落在 4px 网格（2px 只属于描边与指示条）。</summary>
    [Fact]
    public void Semantic_spacing_sits_on_the_four_pixel_grid()
    {
        foreach (var value in new[]
                 {
                     DesignTokens.Space1, DesignTokens.Space2, DesignTokens.Space3, DesignTokens.Space4,
                     DesignTokens.Space5, DesignTokens.Space6, DesignTokens.Space8, DesignTokens.Space12,
                 })
        {
            Assert.Equal(0, value % 4);
        }
    }

    /// <summary>票 19：新图标档只取 Segoe Fluent 的设计尺寸。</summary>
    [Fact]
    public void Icon_sizes_match_the_symbol_font_design_sizes()
    {
        foreach (var size in new[]
                 {
                     DesignTokens.IconXs, DesignTokens.IconS, DesignTokens.IconM,
                     DesignTokens.IconL, DesignTokens.IconXl,
                 })
        {
            Assert.Contains(size, new[] { 12.0, 16.0, 20.0, 24.0, 48.0 });
        }
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
