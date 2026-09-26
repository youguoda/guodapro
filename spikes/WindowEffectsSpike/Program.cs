using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace WindowEffectsSpike;

/// <summary>
/// Shows one of six panel recipes at a fixed screen rectangle and prints,
/// on stdout, whether each DWM attribute was accepted. The probe screenshot
/// then judges the pixels.
///
/// Recipes:
///   1  AllowsTransparency + rounded Border          — the known fallback
///   2  DWM Acrylic backdrop + DWM rounding          — no AllowsTransparency
///   3  DWM Mica backdrop + DWM rounding             — no AllowsTransparency
///   4  DWM Acrylic + WPF semi-opaque content on top — blend both worlds
///   5  AllowsTransparency + DWM Acrylic             — layered window + material
///   6  AllowsTransparency + DWM Mica                — layered window + material
///
/// Recipes 5/6 also show a striped underlay window behind the panel: a blur
/// material would smear the stripes, raw alpha would leave them crisp. The
/// underlay lives in this same process because a WPF window shown from a
/// PowerShell probe without a dispatcher pump paints as plain white.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var recipe = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 2;

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        Window? underlay = null;
        if (recipe is 5 or 6)
        {
            underlay = BuildStripedUnderlay();
            underlay.Show();
        }

        var window = Build(recipe);
        window.Show();
        var details = ApplyDwm(window, recipe);
        Console.WriteLine($"recipe={recipe} dwm: {details} hwnd={new WindowInteropHelper(window).Handle}");

        // Auto-close: the probe screenshots within this window.
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(14),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            underlay?.Close();
            window.Close();
            app.Shutdown();
        };
        timer.Start();

        return app.Run();
    }

    private static Window Build(int recipe)
    {
        var window = new Window
        {
            Title = "effects-spike",
            Width = 420,
            Height = 260,
            Left = 200,
            Top = 200,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
        };

        if (recipe == 1 || recipe is 5 or 6)
        {
            window.AllowsTransparency = true;
            window.Background = Brushes.Transparent;

            // Faint fill for 5/6: enough to prove the panel is there, thin
            // enough that a backdrop material — or the raw stripes — shows
            // through and identifies itself.
            var alpha = recipe == 1 ? 242 : 48;
            var tint = recipe == 1 ? 32 : 24;
            var corner = new CornerRadius(12);
            var fill = new SolidColorBrush(Color.FromArgb((byte)alpha, (byte)tint, (byte)tint, (byte)(tint + 4)));

            var border = new Border
            {
                CornerRadius = corner,
                Background = fill,
                Child = Label(recipe switch
                {
                    1 => "1 AllowsTransparency (fallback)",
                    5 => "5 Layered + Acrylic",
                    _ => "6 Layered + Mica",
                }),
            };
            window.Content = border;
        }
        else
        {
            // No AllowsTransparency: that WS_EX_LAYERED shape is exactly what
            // has historically silenced DWM backdrops. A nearly-transparent
            // background keeps the WPF surface valid while letting the DWM
            // material show through. Recipe 4 layers semi-opaque content on
            // top of the same material.
            window.Background = new SolidColorBrush(Color.FromArgb(2, 0, 0, 0));

            var host = new Grid();
            if (recipe == 4)
            {
                var panel = new Border
                {
                    CornerRadius = new CornerRadius(12),
                    Background = new SolidColorBrush(Color.FromArgb(235, 24, 24, 28)),
                    Margin = new Thickness(10),
                    Child = Label("4 Acrylic + semi-opaque content"),
                };
                host.Children.Add(panel);
            }
            else
            {
                host.Children.Add(Label(recipe == 2 ? "2 DWM Acrylic" : "3 DWM Mica"));
            }

            window.Content = host;
        }

        return window;
    }

    /// <summary>
    /// Hard-edged diagonal magenta/white bands at the panel rect. Blur smears
    /// hard edges; plain alpha does not.
    /// </summary>
    private static Window BuildStripedUnderlay()
    {
        var stripe = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        for (var i = 0; i <= 10; i++)
        {
            var offset = i / 10.0;
            var color = i % 2 == 0 ? Colors.Magenta : Colors.White;
            stripe.GradientStops.Add(new GradientStop(color, offset));
        }

        return new Window
        {
            Title = "effects-underlay",
            Width = 420,
            Height = 260,
            Left = 200,
            Top = 200,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Background = stripe,
        };
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = Brushes.White,
        FontSize = 20,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static string ApplyDwm(Window window, int recipe)
    {
        if (recipe == 1)
        {
            return "skipped (fallback recipe)";
        }

        var helper = new WindowInteropHelper(window);
        _ = helper.EnsureHandle();

        var notes = new List<string>();

        // DWMWA_WINDOW_CORNER_PREFERENCE = 33; DWMWCP_ROUND = 2.
        var corner = 2;
        var cornerOk = Native.DwmSetWindowAttribute(helper.Handle, 33, ref corner, sizeof(int));
        notes.Add($"corner=0x{cornerOk:X}");

        if (recipe is 2 or 4 or 5)
        {
            // DWMWA_SYSTEMBACKDROP_TYPE = 38; DWMSBT_TRANSIENTWINDOW (Acrylic) = 3.
            var backdrop = 3;
            var backdropOk = Native.DwmSetWindowAttribute(helper.Handle, 38, ref backdrop, sizeof(int));
            notes.Add($"acrylic=0x{backdropOk:X}");
        }
        else
        {
            // DWMSBT_MAINWINDOW (Mica) = 2.
            var backdrop = 2;
            var backdropOk = Native.DwmSetWindowAttribute(helper.Handle, 38, ref backdrop, sizeof(int));
            notes.Add($"mica=0x{backdropOk:X}");
        }

        return string.Join(" ", notes);
    }

    private static partial class Native
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
