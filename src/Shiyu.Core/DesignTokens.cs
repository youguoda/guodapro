namespace Shiyu.Core;

/// <summary>How the interface's colours are chosen.</summary>
// Not "ThemeMode": WPF 9 ships its own System.Windows.ThemeMode, and the
// collision would cost every using file an alias.
public enum AppTheme
{
    /// <summary>Follow the Windows personalization setting, live.</summary>
    System,

    Light,

    Dark,
}

/// <summary>
/// One theme's colours by semantic slot. Both shipped palettes define exactly
/// the same slots — see DesignTokenTests for the enforcement.
/// </summary>
public sealed record ThemePalette(string Name, IReadOnlyDictionary<string, string> Colors);

/// <summary>
/// Every visual value the interface is allowed to draw with, in one place.
///
/// Tuned for Chinese-first text rather than inherited from English defaults:
/// body line height 1.7 (not 1.5) so unspaced characters do not clump;
/// hierarchy carried by size and colour rather than weight, because Chinese
/// 500/600 are near-indistinguishable at UI sizes; font chains that name a
/// CJK fallback so mixed text does not jump baseline.
/// </summary>
public static class DesignTokens
{
    // --- colour slots ---------------------------------------------------------

    public const string Background = "Background";
    public const string Surface = "Surface";
    public const string SurfaceInput = "SurfaceInput";
    public const string SurfaceSubtle = "SurfaceSubtle";
    public const string Border = "Border";
    public const string Text = "Text";
    public const string TextSecondary = "TextSecondary";
    public const string TextTertiary = "TextTertiary";
    public const string Accent = "Accent";
    public const string AccentFloating = "AccentFloating";
    public const string TextOnAccent = "TextOnAccent";
    public const string Danger = "Danger";

    /// <summary>The slots every palette must define, and the only slots any window may use.</summary>
    public static readonly string[] Slots =
    [
        Background, Surface, SurfaceInput, SurfaceSubtle, Border,
        Text, TextSecondary, TextTertiary,
        Accent, AccentFloating, TextOnAccent, Danger,
    ];

    /// <summary>
    /// Foreground/background combinations the interface actually draws.
    /// Every pair must stay at WCAG AA (4.5) in every theme — the test turns
    /// a palette tweak that quietly ruins a label into a red build.
    /// </summary>
    public static readonly (string Foreground, string Background)[] ReadablePairs =
    [
        (Text, Background),
        (TextSecondary, Background),
        (TextTertiary, Background),
        (Text, Surface),
        (TextSecondary, Surface),
        (TextTertiary, Surface),
        (Text, SurfaceInput),
        (TextSecondary, SurfaceSubtle),
        (TextOnAccent, Accent),
        (Danger, Background),
        (Danger, Surface),
    ];

    public static ThemePalette Light { get; } = new("Light", new Dictionary<string, string>
    {
        [Background] = "#FFF7F8FA",
        [Surface] = "#FFFFFFFF",
        [SurfaceInput] = "#FFEAECEF",
        [SurfaceSubtle] = "#FFE9EDF1",
        [Border] = "#FFD0D7DE",
        [Text] = "#FF1F2328",
        [TextSecondary] = "#FF57606A",
        [TextTertiary] = "#FF62707B",
        [Accent] = "#FF1A66DB",
        [AccentFloating] = "#EE1A66DB",
        [TextOnAccent] = "#FFFFFFFF",
        [Danger] = "#FFC0392B",
    });

    public static ThemePalette Dark { get; } = new("Dark", new Dictionary<string, string>
    {
        [Background] = "#FF1C2128",
        [Surface] = "#FF22272E",
        [SurfaceInput] = "#FF2A3038",
        [SurfaceSubtle] = "#FF262C34",
        [Border] = "#FF3D444D",
        [Text] = "#FFE6E8EB",
        [TextSecondary] = "#FFB5BCC4",
        [TextTertiary] = "#FF9BA4AD",
        [Accent] = "#FF4C8DFF",
        [AccentFloating] = "#EE4C8DFF",
        [TextOnAccent] = "#FF0B1220",
        [Danger] = "#FFF0675C",
    });

    // --- typography -----------------------------------------------------------

    /// <summary>Latin-first with an explicit CJK fallback, so fallback is deterministic, not linked per glyph.</summary>
    public const string FamilyUi = "Segoe UI, Microsoft YaHei UI";

    /// <summary>Mono-first for code, colours, hotkeys, paths — then CJK for the mixed cases.</summary>
    public const string FamilyMono = "Cascadia Mono, Consolas, Microsoft YaHei UI";

    public const double FontHint = 11;
    public const double FontCaption = 12;
    public const double FontSecondary = 13;
    public const double FontBody = 14;
    public const double FontBodyLarge = 15;

    /// <summary>Chinese body line height. A floor, not a suggestion — see DesignTokenTests.</summary>
    public const double BodyLineRatio = 1.7;

    public static double LineHeightFor(double fontSize)
        => Math.Round(fontSize * BodyLineRatio);

    // --- shape ----------------------------------------------------------------

    /// <summary>Corner radii, named by where they belong rather than by pixel value.</summary>
    public static readonly IReadOnlyDictionary<string, double> Radius = new Dictionary<string, double>
    {
        ["Thumb"] = 3,
        ["Small"] = 6,
        ["Card"] = 10,
        ["Pill"] = 13,
    };

    /// <summary>Drop shadows: (blur radius, depth, opacity). Two strengths, no more.</summary>
    public static readonly (double Blur, double Depth, double Opacity) ShadowFloating = (18, 2, 0.25);
    public static readonly (double Blur, double Depth, double Opacity) ShadowBadge = (10, 1, 0.35);

    /// <summary>
    /// The spacing scale. Anything not on it is layout, not spacing — values
    /// tied to a control's width (a label's indent) stay where they are used.
    /// </summary>
    public static readonly double[] SpacingScale = [2, 4, 6, 8, 10, 12, 14, 16, 18, 24];
}
