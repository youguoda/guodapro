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

    /// <summary>
    /// The translucent brand tint drawn over a DWM material (ticket 03's
    /// recipe): the material paints behind the window, this tint keeps text
    /// readable and the brand present. Alpha carries the meaning — an opaque
    /// value here would switch the material off.
    /// </summary>
    public const string SurfaceMaterial = "SurfaceMaterial";

    /// <summary>The elevated, opaque surface for menus and popups.</summary>
    public const string LayerFlyout = "LayerFlyout";

    /// <summary>Interaction-state overlays: drawn over whatever is beneath.</summary>
    public const string StateHover = "StateHover";
    public const string StatePressed = "StatePressed";

    public const string Border = "Border";
    public const string Text = "Text";
    public const string TextSecondary = "TextSecondary";
    public const string TextTertiary = "TextTertiary";
    public const string Accent = "Accent";
    public const string AccentHover = "AccentHover";
    public const string AccentPressed = "AccentPressed";
    public const string AccentFloating = "AccentFloating";
    public const string TextOnAccent = "TextOnAccent";
    public const string Danger = "Danger";

    /// <summary>The slots every palette must define, and the only slots any window may use.</summary>
    public static readonly string[] Slots =
    [
        Background, Surface, SurfaceInput, SurfaceSubtle, SurfaceMaterial, LayerFlyout,
        StateHover, StatePressed, Border,
        Text, TextSecondary, TextTertiary,
        Accent, AccentHover, AccentPressed, AccentFloating, TextOnAccent, Danger,
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

        // 票 18（评审 §3.6）：词典卡例句/同义词曾用 TextTertiary 压在
        // SurfaceSubtle 上，实测 4.33:1——界面已改画次级色，这一对同时入表，
        // 让调色板再也不能悄悄把它调回不足。
        (TextTertiary, SurfaceSubtle),

        // The material tint is translucent: the test blends it over the theme's
        // Background, which is what the acrylic behind the window effectively is.
        (Text, SurfaceMaterial),
        (TextSecondary, SurfaceMaterial),

        (TextOnAccent, Accent),
        (TextOnAccent, AccentHover),
        (TextOnAccent, AccentPressed),
        (Danger, Background),
        (Danger, Surface),
    ];

    public static ThemePalette Light { get; } = new("Light", new Dictionary<string, string>
    {
        [Background] = "#FFF7F8FA",
        [Surface] = "#FFFFFFFF",
        [SurfaceInput] = "#FFEAECEF",
        [SurfaceSubtle] = "#FFE9EDF1",
        [SurfaceMaterial] = "#F0FCFCFD",
        [LayerFlyout] = "#FFFDFDFE",
        [StateHover] = "#0D1F2328",
        [StatePressed] = "#171F2328",
        [Border] = "#FFD0D7DE",
        [Text] = "#FF1F2328",
        [TextSecondary] = "#FF57606A",
        // 票 18：原 #FF62707B 在 SurfaceSubtle 上只有 4.33:1；加深一档到
        // 4.59:1，让上面新入表的 (TextTertiary, SurfaceSubtle) 过 AA。
        [TextTertiary] = "#FF5E6C77",
        [Accent] = "#FF1A66DB",
        [AccentHover] = "#FF2A6FD8",
        [AccentPressed] = "#FF1557C4",
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
        [SurfaceMaterial] = "#F01E242C",
        [LayerFlyout] = "#FF252B33",
        [StateHover] = "#0DE6E8EB",
        [StatePressed] = "#17E6E8EB",
        [Border] = "#FF3D444D",
        [Text] = "#FFE6E8EB",
        [TextSecondary] = "#FFB5BCC4",
        [TextTertiary] = "#FF9BA4AD",
        [Accent] = "#FF4C8DFF",
        [AccentHover] = "#FF669DFF",
        [AccentPressed] = "#FF3B7DF2",
        [AccentFloating] = "#EE4C8DFF",
        [TextOnAccent] = "#FF0B1220",
        [Danger] = "#FFF0675C",
    });

    // --- typography -----------------------------------------------------------

    /// <summary>Latin-first with an explicit CJK fallback, so fallback is deterministic, not linked per glyph.</summary>
    public const string FamilyUi = "Segoe UI, Microsoft YaHei UI";

    /// <summary>Mono-first for code, colours, hotkeys, paths — then CJK for the mixed cases.</summary>
    public const string FamilyMono = "Cascadia Mono, Consolas, Microsoft YaHei UI";

    /// <summary>
    /// The system symbol font, Windows 11's answer to what SF Symbols is on the
    /// Mac: glyphs that ship with the OS and follow its design language. The
    /// fallback keeps Windows 10 on the same codepoints (Segoe MDL2 Assets)
    /// without a version branch anywhere in the interface code.
    /// </summary>
    public const string FamilyIcon = "Segoe Fluent Icons, Segoe MDL2 Assets";

    public const double FontHint = 14;
    public const double FontCaption = 14;
    public const double FontSecondary = 15;
    public const double FontBody = 16;
    public const double FontBodyLarge = 18;

    /// <summary>Sizes for the symbol font: inline with text, standard, feature.</summary>
    public const double IconSmall = 14;
    public const double IconMedium = 16;
    public const double IconLarge = 19;

    /// <summary>Chinese body line height. A floor, not a suggestion — see DesignTokenTests.</summary>
    public const double BodyLineRatio = 1.7;

    public static double LineHeightFor(double fontSize)
        => Math.Round(fontSize * BodyLineRatio);

    // --- shape ----------------------------------------------------------------

    /// <summary>
    /// Corner radii, named by where they belong rather than by pixel value.
    /// The 4/8 pair is Windows 11's ControlCornerRadius / OverlayCornerRadius;
    /// 12 is the floating-window tier the tickets settled on.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, double> Radius = new Dictionary<string, double>
    {
        ["Thumb"] = 3,
        ["Small"] = 4,
        ["Card"] = 8,
        ["Overlay"] = 8,
        ["Window"] = 12,
        ["Pill"] = 13,
    };

    /// <summary>
    /// Drop shadows: (blur radius, depth, opacity). Two strengths, no more —
    /// tuned to the Windows 11 flyout look: wide, soft, low.
    /// </summary>
    public static readonly (double Blur, double Depth, double Opacity) ShadowFloating = (26, 5, 0.2);
    public static readonly (double Blur, double Depth, double Opacity) ShadowBadge = (14, 3, 0.3);

    /// <summary>
    /// The spacing scale. Anything not on it is layout, not spacing — values
    /// tied to a control's width (a label's indent) stay where they are used.
    /// </summary>
    public static readonly double[] SpacingScale = [2, 4, 6, 8, 10, 12, 14, 16, 18, 24];

    // --- motion -----------------------------------------------------------------

    /// <summary>State changes. Fast enough to read as response, not as movement.</summary>
    public const double MotionFastMs = 160;

    /// <summary>Feature moments. Slower, never slow enough to wait for.</summary>
    public const double MotionSlowMs = 300;

    public static TimeSpan MotionFast => TimeSpan.FromMilliseconds(MotionFastMs);
    public static TimeSpan MotionSlow => TimeSpan.FromMilliseconds(MotionSlowMs);

    /// <summary>
    /// Content-layer entrance for transient surfaces: the window's own opacity
    /// stays at 1 (a layered window fading from transparent never composites —
    /// ticket 04's badge finding), so the entrance moves the content instead.
    /// </summary>
    public const double EntranceShift = 8;
}

/// <summary>
/// The two motion curves. A new effect that fits neither curve is a new
/// effect that should not exist.
/// </summary>
public enum MotionCurve
{
    /// <summary>Ordinary state changes: calm, quick, out of the way.</summary>
    Standard,

    /// <summary>The two or three "watch this" moments: a little more personality, same family.</summary>
    Feature,
}

/// <summary>
/// Decides how long a transition takes. Every animation in the app asks here,
/// which is what makes reduced motion all-or-nothing rather than a sieve: one
/// call site forgetting to consult this is one effect that ignores the user's
/// accessibility preference.
/// </summary>
public static class MotionPlan
{
    /// <param name="animationsAllowed">What the system says; supplied live by the platform layer.</param>
    /// <param name="feature">True for the two or three moments that earn the slower tier.</param>
    public static TimeSpan Duration(bool animationsAllowed, bool feature = false)
        => !animationsAllowed ? TimeSpan.Zero
         : feature ? DesignTokens.MotionSlow
         : DesignTokens.MotionFast;
}
