using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// Owns the application-level resource dictionaries.
///
/// Two dictionaries are merged at startup: <see cref="ValuesDictionaryName"/>
/// holds every token VALUE, compiled from Core's DesignTokens, and
/// Themes/Controls.xaml holds the control styles that reference those tokens
/// dynamically. Switching themes swaps only the value dictionary;
/// DynamicResource references then resolve to the new brushes in place — no
/// window is rebuilt and nothing flickers.
///
/// Merging is done in code rather than in App.xaml on purpose: this machine's
/// PresentationBuildTasks emits an App.g.cs without InitializeComponent, where
/// App.xaml markup — including declared resources — never loads (recorded in
/// ticket 02).
/// </summary>
internal sealed class ThemeManager : IDisposable
{
    private const string ControlsSource = "/Themes/Controls.xaml";
    private static readonly TimeSpan SystemWatchInterval = TimeSpan.FromSeconds(5);

    private readonly System.Windows.Threading.DispatcherTimer _systemWatch = new()
    {
        Interval = SystemWatchInterval,
    };

    private AppTheme _mode;
    private string _applied;

    /// <summary>Whether the palette currently applied is Dark — Backdrop reads it.</summary>
    public static bool CurrentIsDark { get; private set; }

    public ThemeManager()
    {
        _applied = string.Empty;

        Application.Current.Resources.MergedDictionaries.Add(BuildValues(DesignTokens.Light));
        Application.Current.Resources.MergedDictionaries.Add(
            new ResourceDictionary { Source = new Uri(ControlsSource, UriKind.Relative) });

        _systemWatch.Tick += (_, _) => FollowSystemIfItMoved();
    }

    /// <summary>Applies the mode now; while it is System, keeps following Windows live.</summary>
    public void Apply(AppTheme mode)
    {
        _mode = mode;
        SwapTo(Resolve(mode));
        _systemWatch.IsEnabled = mode == AppTheme.System;
    }

    private void FollowSystemIfItMoved()
    {
        var effective = Resolve(_mode);

        // Reading the registry every few seconds is cheap; swapping only on a
        // real change keeps an idle theme alone.
        if (effective.Name != _applied)
        {
            SwapTo(effective);
        }
    }

    private static ThemePalette Resolve(AppTheme mode)
        => mode == AppTheme.Dark || (mode == AppTheme.System && SystemPrefersDark())
            ? DesignTokens.Dark
            : DesignTokens.Light;

    /// <summary>
    /// Windows 10 1607+ records the user's app colour preference here; a missing
    /// value is treated as light, which is the historical default.
    /// </summary>
    internal static bool SystemPrefersDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

        return key?.GetValue("AppsUseLightTheme") is int useLight && useLight == 0;
    }

    private void SwapTo(ThemePalette palette)
    {
        _applied = palette.Name;
        CurrentIsDark = palette.Name == "Dark";

        var dictionaries = Application.Current.Resources.MergedDictionaries;

        // Index 0 is the value dictionary this class installed; the control
        // style dictionary after it stays put. Replacing the reference (rather
        // than clearing keys in place) lets WPF hand every DynamicResource the
        // new brushes atomically.
        dictionaries[0] = BuildValues(palette);

        // The DWM backdrop's dark flag rides along with every palette swap:
        // a Mica/Acrylic surface left in the wrong mode is instantly wrong.
        Backdrop.SyncToTheme(CurrentIsDark);
    }

    private static ResourceDictionary BuildValues(ThemePalette palette)
    {
        var values = new ResourceDictionary();

        foreach (var slot in DesignTokens.Slots)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette.Colors[slot]));
            brush.Freeze();
            values[$"Brush.{slot}"] = brush;
        }

        values["Font.Ui"] = new FontFamily(DesignTokens.FamilyUi);
        values["Font.Mono"] = new FontFamily(DesignTokens.FamilyMono);
        values["Font.Icon"] = new FontFamily(DesignTokens.FamilyIcon);

        values["Size.Hint"] = DesignTokens.FontHint;
        values["Size.Caption"] = DesignTokens.FontCaption;
        values["Size.Secondary"] = DesignTokens.FontSecondary;
        values["Size.Body"] = DesignTokens.FontBody;
        values["Size.BodyLarge"] = DesignTokens.FontBodyLarge;
        values["Size.IconSmall"] = DesignTokens.IconSmall;
        values["Size.IconMedium"] = DesignTokens.IconMedium;
        values["Size.IconLarge"] = DesignTokens.IconLarge;

        values["Line.Secondary"] = DesignTokens.LineHeightFor(DesignTokens.FontSecondary);
        values["Line.Body"] = DesignTokens.LineHeightFor(DesignTokens.FontBody);
        values["Line.BodyLarge"] = DesignTokens.LineHeightFor(DesignTokens.FontBodyLarge);

        values["Weight.Emphasis"] = FontWeights.SemiBold;

        foreach (var (name, radius) in DesignTokens.Radius)
        {
            values[$"Radius.{name}"] = new CornerRadius(radius);
        }

        values["Shadow.Floating"] = Shadow(DesignTokens.ShadowFloating);
        values["Shadow.Badge"] = Shadow(DesignTokens.ShadowBadge);

        return values;

        static DropShadowEffect Shadow((double Blur, double Depth, double Opacity) spec)
        {
            var effect = new DropShadowEffect
            {
                BlurRadius = spec.Blur,
                ShadowDepth = spec.Depth,
                Opacity = spec.Opacity,
            };
            effect.Freeze();
            return effect;
        }
    }

    public void Dispose() => _systemWatch.Stop();
}
