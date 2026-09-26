using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>
/// The full-content preview panel (ticket 17): hold Space or rest the pointer
/// on a card, and the whole entry appears beside the bar — text scrollable,
/// images at true proportion, files with every path.
///
/// The panel opens at its final size. That size is computed before anything is
/// shown, from numbers the database already has: wrapped line count for text
/// (measured with the real font, which is arithmetic, not layout), pixel size
/// for images, row count for files. A window that grew after appearing would
/// read as a guess.
///
/// Like the badge it never activates, so reading it costs the user nothing.
/// Keys therefore arrive at the bar that owns it, not here.
/// </summary>
internal partial class PreviewWindow : Window
{
    private readonly FileTypeIcons _fileIcons;

    /// <summary>The entry the panel is showing right now.</summary>
    public long CardId { get; private set; }

    /// <summary>Raised when the pointer comes to rest on the panel — the bar suspends its close buffer.</summary>
    public event Action? PointerRestingOnPanel;

    public event Action? PointerLeftPanel;

    public PreviewWindow(FileTypeIcons fileIcons)
    {
        InitializeComponent();
        _fileIcons = fileIcons;
        ApplyThemedSurface();
    }

    /// <summary>
    /// The panel's translucent skin over the theme's own surface colour. The
    /// translucency is the ticket's ask; the colour must follow the theme or
    /// a light theme puts its dark text on this panel's dark paint.
    /// </summary>
    private void ApplyThemedSurface()
    {
        if (FindResource("Brush.Surface") is SolidColorBrush surface)
        {
            var colour = surface.Color;
            Root.Background = new SolidColorBrush(Color.FromArgb(0xF2, colour.R, colour.G, colour.B));
        }

        if (FindResource("Brush.Border") is SolidColorBrush border)
        {
            var colour = border.Color;
            Root.BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, colour.R, colour.G, colour.B));
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var helper = new WindowInteropHelper(this);
        TransientWindow.MakeNonActivating(helper.Handle);
        DwmEffects.TryApplyPanel(helper.Handle);

        // WS_EX_NOACTIVATE alone is not enough: a click that lands on
        // focusable content inside (a scrollable body, say) makes WPF raise
        // the window to foreground anyway. Answering WM_MOUSEACTIVATE with
        // MA_NOACTIVATE closes that path at the source — reading the panel
        // never costs the user their caret.
        if (HwndSource.FromHwnd(helper.Handle) is { } source)
        {
            source.AddHook(RefuseActivation);
        }
    }

    private const int WmMouseActivate = 0x21;

    private const int MaNoActivate = 3;

    private static IntPtr RefuseActivation(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmMouseActivate)
        {
            handled = true;
            return (IntPtr)MaNoActivate;
        }

        return IntPtr.Zero;
    }

    // --- showing ----------------------------------------------------------------

    /// <summary>
    /// Shows (or moves) the panel for a card, anchored beside a card rectangle
    /// in physical pixels. The size is decided first, the position second, and
    /// the content last — the shape never changes once on screen.
    ///
    /// The DPI scale comes from the caller: before this window's first show it
    /// has no presentation source of its own to read one from, and a guessed
    /// 1.0 would place a 1.5x panel as if it were a third narrower.
    /// </summary>
    public void ShowFor(BarCard card, ScreenRect anchor, bool slide, double scaleX, double scaleY)
    {
        var (width, height) = Measure(card);
        Width = width;
        Height = height;

        var wasVisible = IsVisible;
        Fill(card);
        CardId = card.Id;

        var placed = PreviewPlacement.Place(
            anchor,
            (int)Math.Ceiling(width * scaleX),
            (int)Math.Ceiling(height * scaleY),
            ScreenGeometry.WorkAreaAt(new ScreenPoint(
                (anchor.Left + anchor.Right) / 2,
                (anchor.Top + anchor.Bottom) / 2)));

        if (!wasVisible)
        {
            // The spike's rule: a layered window appears at full opacity — a
            // fade-in from transparent never composites. First placement is a
            // jump, not a slide: there is nothing to slide from.
            Opacity = 1;
            BeginAnimation(OpacityProperty, null);
            var helper = new WindowInteropHelper(this);
            _ = helper.EnsureHandle();
            TransientWindow.MoveTo(helper.Handle, placed);
            Show();
            return;
        }

        if (slide && UiAnimation.Allowed())
        {
            // A retarget glides: the same panel carried across reads as one
            // continuous thing, where a close-and-reopen reads as flicker.
            // The glide is driven in physical pixels on purpose — WPF's
            // Left/Top know nothing of a window positioned by SetWindowPos
            // and would snap it back to a stale value mid-animation.
            SlideTo(placed);
        }
        else
        {
            TransientWindow.MoveTo(new WindowInteropHelper(this).Handle, placed);
        }
    }

    private System.Windows.Threading.DispatcherTimer? _slide;

    /// <summary>
    /// Lerps the window to its new spot with SetWindowPos over the standard
    /// fast duration — reduced motion lands here as one instant step, the
    /// same deal every other motion in the app gets.
    /// </summary>
    private void SlideTo(ScreenPoint target)
    {
        _slide?.Stop();

        var handle = new WindowInteropHelper(this).Handle;
        if (GetWindowRect(handle, out var current))
        {
            var start = new ScreenPoint(current.Left, current.Top);
            var duration = MotionPlan.Duration(animationsAllowed: true);
            var elapsed = TimeSpan.Zero;
            var clock = Stopwatch.StartNew();

            _slide = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16),
            };

            _slide.Tick += (_, _) =>
            {
                elapsed = clock.Elapsed;
                if (elapsed >= duration)
                {
                    _slide.Stop();
                    TransientWindow.MoveTo(handle, target);
                    return;
                }

                var progress = duration.Ticks == 0 ? 1.0 : (double)elapsed.Ticks / duration.Ticks;
                var eased = 1 - Math.Pow(1 - progress, 3);

                TransientWindow.MoveTo(handle, new ScreenPoint(
                    (int)Math.Round(start.X + (target.X - start.X) * eased),
                    (int)Math.Round(start.Y + (target.Y - start.Y) * eased)));
            };

            _slide.Start();
        }
        else
        {
            TransientWindow.MoveTo(handle, target);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>Removes the panel. The fade starts from a painted surface, so it composites.</summary>
    public void TakeDown()
    {
        if (!IsVisible)
        {
            return;
        }

        var fade = Motion.Fade(0);
        fade.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void OnMouseEnter(object sender, MouseEventArgs e) => PointerRestingOnPanel?.Invoke();

    private void OnMouseLeave(object sender, MouseEventArgs e) => PointerLeftPanel?.Invoke();

    // --- sizing -------------------------------------------------------------------

    /// <summary>
    /// The panel's final size, from <see cref="PreviewSizing"/> with the
    /// caller-measured text numbers. Images hand in their stored pixels; files
    /// their row count.
    /// </summary>
    private (double Width, double Height) Measure(BarCard card)
    {
        var lineHeight = DesignTokens.LineHeightFor(DesignTokens.FontBody);

        switch (card.Kind)
        {
            case EntryKind.Image:
                return PreviewSizing.ForImage(card.PixelWidth, card.PixelHeight);

            case EntryKind.Files:
                return PreviewSizing.ForFiles(card.Files.Count, FileRowHeight);

            default:
                var box = PreviewSizing.MaxWidth - PreviewSizing.ChromeHorizontal;
                var formatted = Formatted(
                    card.Text,
                    DesignTokens.FontBody,
                    constrain: box);
                var lineCount = (int)Math.Ceiling(formatted.Height / lineHeight);
                var textWidth = Math.Min(formatted.Width, box);

                return PreviewSizing.ForText(lineCount, textWidth, lineHeight);
        }
    }

    /// <summary>One file row: a line and its breathing room, in the secondary size.</summary>
    private const double FileRowHeight = 26;

    /// <summary>
    /// Measures wrapped text with the real font. This is the "worked out, not
    /// laid out" half of the size rule: no control is created, nothing is
    /// shown, the arithmetic just runs.
    /// </summary>
    private static FormattedText Formatted(string text, double size, double constrain)
    {
        var typeface = new Typeface(DesignTokens.FamilyUi);

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            Brushes.Transparent,
            pixelsPerDip: 1.0)
        {
            MaxTextWidth = constrain,
        };

        return formatted;
    }

    // --- content -------------------------------------------------------------------

    private void Fill(BarCard card)
    {
        KindText.Text = card.Kind == EntryKind.Image && card.PixelWidth > 0
            ? $"{card.KindText} · {card.PixelWidth}×{card.PixelHeight}"
            : card.KindText;
        WhenText.Text = card.WhenText;

        TextHost.Visibility = card.Kind == EntryKind.Text ? Visibility.Visible : Visibility.Collapsed;
        ImageHost.Visibility = card.Kind == EntryKind.Image ? Visibility.Visible : Visibility.Collapsed;
        FilesHost.Visibility = card.Kind == EntryKind.Files ? Visibility.Visible : Visibility.Collapsed;

        TextBody.Text = card.Text;
        FilesBody.Children.Clear();

        if (card.Kind == EntryKind.Image)
        {
            ImageHost.Source = LoadImage(card);
        }

        if (card.Kind == EntryKind.Files)
        {
            // Every path, not the card's clamp: "which file was that" is
            // answered by the list, and a path that no longer exists says so
            // here the same way the card does there.
            foreach (var path in card.Files)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };

                var icon = new Image
                {
                    Source = _fileIcons.For(path),
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Focusable = false,
                };
                RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
                row.Children.Add(icon);

                var dead = !File.Exists(path);
                var name = new TextBlock
                {
                    Text = path,
                    FontSize = DesignTokens.FontSecondary,
                    Foreground = (Brush)FindResource("Brush.Text"),
                    VerticalAlignment = VerticalAlignment.Center,
                };

                if (dead)
                {
                    name.TextDecorations = System.Windows.TextDecorations.Strikethrough;
                    name.Opacity = 0.5;
                    name.ToolTip = "路径不存在";
                }

                row.Children.Add(name);
                FilesBody.Children.Add(row);
            }
        }
    }

    /// <summary>
    /// The original at full fidelity when it still exists, decoded no larger
    /// than the panel needs; the forever-kept thumbnail otherwise.
    /// </summary>
    private ImageSource? LoadImage(BarCard card)
    {
        var boxWidth = Math.Max(1, (int)(Width - PreviewSizing.ChromeHorizontal));

        if (card.OriginalPath is { Length: > 0 } path && File.Exists(path))
        {
            try
            {
                return Decode(new Uri(path), boxWidth);
            }
            catch (Exception)
            {
                // A corrupt or truncated original falls back below; the
                // thumbnail is the promise the database always keeps.
            }
        }

        if (card.Thumbnail is { } thumbnail)
        {
            return thumbnail;
        }

        return null;
    }

    private static BitmapSource Decode(Uri source, int decodeWidth)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = decodeWidth;
        image.UriSource = source;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
