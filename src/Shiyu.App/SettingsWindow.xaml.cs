using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>
/// The settings surface, rendered from the schema tree: pages by user intent,
/// every editor built from its declaration. The window hardcodes no setting —
/// it knows control shapes, not settings. Adding one is a schema leaf plus a
/// value binding; this file does not change.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _current;
    private readonly Action<AppSettings> _apply;
    private readonly BackupUi? _backup;

    private readonly Dictionary<string, ItemState> _edited = [];
    private readonly Dictionary<string, FrameworkElement> _rows = [];
    private readonly Dictionary<string, TextBox> _numberBoxes = [];
    private readonly Dictionary<string, List<(ToggleButton Button, int Index)>> _segments = [];
    private readonly Dictionary<string, ScrollViewer> _pageScrollers = [];
    private readonly Dictionary<string, int> _pageTabIndex = [];

    private TextBox? _directoryBox;
    private TextBlock? _directoryWarning;
    private PasswordBox? _secretBox;

    /// <summary>The page the user last had open, kept per session.</summary>
    private static int _lastTabIndex;

    public SettingsWindow(AppSettings current, Action<AppSettings> apply, BackupUi? backup = null)
    {
        InitializeComponent();

        _current = current;
        _apply = apply;
        _backup = backup;

        Backdrop.Attach(this, () => BackdropKind.None);

        BuildTree();

        // Reopen where the user left off; the index is clamped by the count
        // so a future schema shrink cannot select a ghost page.
        Pages.SelectionChanged += (_, _) => _lastTabIndex = Pages.SelectedIndex;
        Pages.SelectedIndex = Math.Clamp(_lastTabIndex, 0, Pages.Items.Count - 1);
    }

    // --- building ----------------------------------------------------------------

    private void BuildTree()
    {
        foreach (var page in SettingsSchema.Tree)
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var body = new StackPanel { Margin = new Thickness(0, 4, 8, 0) };

            foreach (var section in page.Sections)
            {
                var heading = new TextBlock { Text = section.Title, Style = (Style)FindResource("SectionHeading") };
                body.Children.Add(heading);

                foreach (var item in section.Items)
                {
                    var row = RowFor(item);
                    _rows[item.Id] = row;
                    body.Children.Add(row);
                }
            }

            scroll.Content = body;
            _pageScrollers[page.Id] = scroll;
            _pageTabIndex[page.Id] = Pages.Items.Count;
            Pages.Items.Add(new TabItem { Header = page.Title, Content = scroll });
        }
    }

    // --- search and deep links --------------------------------------------------

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        ResultsList.Children.Clear();

        var hits = SettingsSearch.Find(SearchBox.Text);
        if (SearchBox.Text.Trim().Length == 0)
        {
            ResultsHost.Visibility = Visibility.Collapsed;
            return;
        }

        ResultsHost.Visibility = Visibility.Visible;

        if (hits.Count == 0)
        {
            var none = new TextBlock
            {
                Text = "没有匹配的设置项——换个说法试试？",
                Margin = new Thickness(8, 6, 8, 6),
                Opacity = 0.8,
            };
            none.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            ResultsList.Children.Add(none);
            return;
        }

        foreach (var hit in hits)
        {
            var captured = hit;
            var button = new Button
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = hit.Item.Label },
                        new TextBlock
                        {
                            Text = $"{hit.PageTitle} · {hit.SectionTitle}",
                            FontSize = (double)FindResource("Size.Hint"),
                            Opacity = 0.75,
                        },
                    },
                },
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.Hand,
            };
            button.SetResourceReference(BackgroundProperty, "Brush.Surface");
            button.Click += (_, _) => JumpTo(captured.Item.Id, captured.PageId);
            ResultsList.Children.Add(button);
        }

        if (hits.Count >= SettingsSearch.ResultCap)
        {
            var cap = new TextBlock
            {
                Text = $"已显示前 {SettingsSearch.ResultCap} 项——再具体一点。",
                Margin = new Thickness(8, 4, 8, 4),
                FontSize = (double)FindResource("Size.Hint"),
                Opacity = 0.7,
            };
            cap.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextTertiary");
            ResultsList.Children.Add(cap);
        }
    }

    /// <summary>
    /// The deep link: one id lands the user on that item — tab selected, row
    /// scrolled to the middle of the view, and a decaying pulse saying
    /// "this one", because landing silently looks like not landing at all.
    /// </summary>
    public void JumpToItem(string itemId)
    {
        var hit = SettingsSchema.Tree
            .SelectMany(page => page.Sections.SelectMany(section => section.Items)
                .Select(item => (page.Id, item)))
            .FirstOrDefault(entry => entry.item.Id == itemId);

        if (hit.item is not null)
        {
            JumpTo(hit.item.Id, hit.Id);
        }
    }

    private void JumpTo(string itemId, string pageId)
    {
        SearchBox.Clear();

        if (!_pageTabIndex.TryGetValue(pageId, out var index)
            || !_rows.TryGetValue(itemId, out var row))
        {
            return;
        }

        // A child under a collapsed parent cannot be shown without flipping
        // the parent's value — not ours to do — so the pulse lands on the
        // deepest ancestor the user can actually see.
        var target = row;
        var candidate = AllItems().FirstOrDefault(item => item.Id == itemId);
        while (candidate is { Parent: { } parentId }
               && _edited.TryGetValue(parentId, out var parent)
               && !parent.Toggle)
        {
            if (!_rows.TryGetValue(parentId, out var parentRow))
            {
                break;
            }

            target = parentRow;
            candidate = AllItems().FirstOrDefault(next => next.Id == parentId);
        }

        Pages.SelectedIndex = index;

        // The tab has to lay out before there is anything to scroll.
        Dispatcher.BeginInvoke(() =>
        {
            if (!_pageScrollers.TryGetValue(pageId, out var scroller))
            {
                return;
            }

            var top = target.TranslatePoint(new Point(0, 0), (UIElement)scroller.Content).Y;
            var centre = top + target.ActualHeight / 2 - scroller.ViewportHeight / 2;
            scroller.ScrollToVerticalOffset(Math.Max(0, centre));

            Pulse(target);
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    /// <summary>
    /// Three decaying flashes rather than one steady glow: steady reads as
    /// "selected", decay reads as "look here". With animations reduced, a
    /// quiet static wash says the same thing without moving.
    /// </summary>
    private static void Pulse(FrameworkElement row)
    {
        if (row is not Grid grid)
        {
            return;
        }

        var wash = new Border
        {
            Background = (Brush)row.FindResource("Brush.Accent"),
            Opacity = 0,
            IsHitTestVisible = false,
            CornerRadius = new CornerRadius(4),
        };
        Grid.SetColumnSpan(wash, 2);
        grid.Children.Add(wash);

        void Remove()
        {
            grid.Children.Remove(wash);
        }

        if (!UiAnimation.Allowed())
        {
            wash.Opacity = 0.16;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.8),
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Remove();
            };
            timer.Start();
            return;
        }

        var pulse = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1.3) };
        foreach (var (at, peak) in new[]
                 {
                     (0.0, 0.0), (0.15, 0.38), (0.45, 0.0),
                     (0.55, 0.22), (0.85, 0.0), (0.95, 0.12), (1.3, 0.0),
                 })
        {
            pulse.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(at))));
        }

        pulse.Completed += (_, _) => Remove();
        wash.BeginAnimation(OpacityProperty, pulse);
    }

    private FrameworkElement RowFor(SettingsItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Margin = new Thickness(0, 0, 0, 6);

        var label = new TextBlock
        {
            Text = item.Label,
            Style = (Style)FindResource("FieldLabel"),
            ToolTip = item.Hint,
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var editor = EditorFor(item);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);

        // An item under a collapsed parent is not merely greyed — it is gone,
        // because greyed controls invite exactly the clicks they refuse.
        if (item.Parent is { } parent && _edited.TryGetValue(parent, out var parentState))
        {
            grid.Visibility = parentState.Toggle ? Visibility.Visible : Visibility.Collapsed;
        }

        return grid;
    }

    private FrameworkElement EditorFor(SettingsItem item)
    {
        var state = new ItemState();
        _edited[item.Id] = state;

        return item.Control switch
        {
            SettingsControl.Segmented => SegmentedFor(item, state),
            SettingsControl.Toggle => ToggleFor(item, state),
            SettingsControl.Number => NumberFor(item, state),
            SettingsControl.Password => SecretFor(item, state),
            SettingsControl.Directory => DirectoryFor(item, state),
            SettingsControl.Multiline => TextFor(item, state, multiline: true),
            SettingsControl.Actions => TextFor(item, state, multiline: false),
            SettingsControl.Hotkey => HotkeyCapture(item, state),
            SettingsControl.Text => TextFor(item, state, multiline: false),
            SettingsControl.ReadOnly => ReadOnlyFor(item),
            SettingsControl.Custom => CustomFor(item),
            _ => new TextBlock(),
        };
    }

    /// <summary>
    /// The hotkey editor as a capture control: click it, press the combination,
    /// done — the same interaction the system's own settings use, and one that
    /// cannot produce the typos a hand-typed "Ctrl+Shift+Z" can. Only letters
    /// and digits register (the spec registers nothing else globally); Esc
    /// leaves the box without changing anything.
    /// </summary>
    private FrameworkElement HotkeyCapture(SettingsItem item, ItemState state)
    {
        var box = new TextBox
        {
            Text = SettingsBindings.ReadText(item.Id, _current) ?? string.Empty,
            Padding = new Thickness(4),
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "点击后直接按下组合键；Esc 取消。仅支持字母/数字键加修饰键。",
        };
        box.SetResourceReference(BackgroundProperty, "Brush.SurfaceInput");
        state.Text = box.Text;

        box.GotFocus += (_, _) => box.SetResourceReference(BorderBrushProperty, "Brush.Accent");
        box.LostFocus += (_, _) => box.SetResourceReference(BorderBrushProperty, "Brush.Border");

        box.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Modifier keys alone are the "listening" state: swallowed so
            // nothing types while the user composes the combination.
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            {
                e.Handled = true;
                return;
            }

            if (key == Key.Escape)
            {
                e.Handled = true;
                Keyboard.ClearFocus();
                return;
            }

            var mods = Keyboard.Modifiers;
            if (mods == ModifierKeys.None || e.IsRepeat)
            {
                e.Handled = true;
                return;
            }

            char? letter = key switch
            {
                >= Key.A and <= Key.Z => (char)('A' + (key - Key.A)),
                >= Key.D0 and <= Key.D9 => (char)('0' + (key - Key.D0)),
                >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (key - Key.NumPad0)),
                _ => null,
            };

            // Unsupported keys (F-keys, punctuation) are refused silently:
            // the spec cannot register them globally anyway.
            if (letter is not { } digit)
            {
                e.Handled = true;
                return;
            }

            var parts = new List<string>();
            if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(digit.ToString());

            state.Text = string.Join("+", parts);
            box.Text = state.Text;
            e.Handled = true;
        };

        return WrapWithHint(box, item.Hint);
    }

    private FrameworkElement SegmentedFor(SettingsItem item, ItemState state)
    {
        state.Choice = SettingsBindings.ReadChoice(item.Id, _current);

        var host = new StackPanel { Orientation = Orientation.Horizontal };
        var buttons = new List<(ToggleButton, int)>();

        for (var index = 0; index < item.ChoiceList.Length; index++)
        {
            var captured = index;
            var button = new ToggleButton
            {
                Content = item.ChoiceList[index],
                Padding = new Thickness(12, 3, 12, 3),
                Cursor = Cursors.Hand,

                // Checked/hover/pressed states live in the shared style; the
                // local value here would override the style's triggers.
                Style = (Style)FindResource("SegmentChip"),
            };
            button.Click += (_, _) => SelectSegment(item.Id, captured);
            buttons.Add((button, index));
            host.Children.Add(button);
        }

        _segments[item.Id] = buttons;
        PaintSegments(item.Id);
        return host;
    }

    private void SelectSegment(string id, int index)
    {
        _edited[id].Choice = index;
        PaintSegments(id);
    }

    /// <summary>A segmented control looks like what it is: one joined row of options.</summary>
    private void PaintSegments(string id)
    {
        if (!_segments.TryGetValue(id, out var buttons))
        {
            return;
        }

        var chosen = _edited[id].Choice;
        foreach (var (button, index) in buttons)
        {
            // All visuals — checked accent, hover overlay, pressed — come from
            // the SegmentChip style's triggers; only the state moves here.
            button.IsChecked = index == chosen;
        }
    }

    private FrameworkElement ToggleFor(SettingsItem item, ItemState state)
    {
        // Built by the shared factory: settings and onboarding render the
        // same tree with the same hands.
        return ItemEditors.Toggle(
            item, _current, state,
            changed: () => ApplyParentVisibility(item.Id, state.Toggle));
    }

    private void ApplyParentVisibility(string parentId, bool on)
    {
        foreach (var other in SettingsSchema.Tree.SelectMany(p => p.Sections).SelectMany(s => s.Items))
        {
            if (other.Parent == parentId && _rows.TryGetValue(other.Id, out var row))
            {
                row.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private FrameworkElement NumberFor(SettingsItem item, ItemState state)
    {
        var box = new TextBox
        {
            Text = SettingsBindings.ReadText(item.Id, _current) ?? string.Empty,
            Width = 70,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        box.SetResourceReference(BackgroundProperty, "Brush.SurfaceInput");
        state.Text = box.Text;

        void MarkDirty(bool dirty)
        {
            // The uncommitted state is visible: an accent border says "what
            // you typed is not yet what will be saved".
            if (dirty)
            {
                box.SetResourceReference(BorderBrushProperty, "Brush.Accent");
            }
            else
            {
                box.SetResourceReference(BorderBrushProperty, "Brush.Border");
            }
        }

        box.TextChanged += (_, _) => MarkDirty(true);

        void Commit()
        {
            if (double.TryParse(box.Text.Trim(), out var value))
            {
                var clamped = Math.Clamp(value, item.Min, item.Max);
                var formatted = clamped == Math.Floor(clamped)
                    ? ((int)clamped).ToString()
                    : clamped.ToString("0.#");
                state.Text = formatted;
                box.Text = formatted;
                MarkDirty(false);
            }
        }

        box.LostFocus += (_, _) => Commit();
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };

        _numberBoxes[item.Id] = box;
        return box;
    }

    private FrameworkElement TextFor(SettingsItem item, ItemState state, bool multiline)
        => ItemEditors.Text(item, _current, state);

    private FrameworkElement SecretFor(SettingsItem item, ItemState state)
    {
        var (editor, box) = ItemEditors.Password(item, _current, state);
        _secretBox = box;
        return editor;
    }

    private FrameworkElement DirectoryFor(SettingsItem item, ItemState state)
    {
        var box = new TextBox
        {
            Text = SettingsBindings.ReadText(item.Id, _current) ?? string.Empty,
            Padding = new Thickness(4),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        box.SetResourceReference(BackgroundProperty, "Brush.SurfaceInput");
        state.Text = box.Text;
        box.TextChanged += (_, _) =>
        {
            state.Text = box.Text;
            UpdateSyncWarning();
        };

        var browse = new Button { Content = "浏览…", Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(6, 0, 0, 0), Cursor = Cursors.Hand };
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择拾语存放数据的位置",
                InitialDirectory = Directory.Exists(box.Text) ? box.Text : AppPaths.DataDirectory,
            };
            if (dialog.ShowDialog(this) == true)
            {
                box.Text = dialog.FolderName;
            }
        };

        // A two-column row so the path box stretches with the page instead of
        // squeezing into its own minimum width.
        var rowGrid = new Grid();
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(box, 0);
        Grid.SetColumn(browse, 1);
        rowGrid.Children.Add(box);
        rowGrid.Children.Add(browse);

        _directoryBox = box;
        _directoryWarning = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = (double)FindResource("Size.Caption"),
            Visibility = Visibility.Collapsed,
        };
        _directoryWarning.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");

        var stack = new StackPanel();
        stack.Children.Add(rowGrid);
        stack.Children.Add(_directoryWarning);
        UpdateSyncWarning();

        return WrapWithHint(stack, item.Hint);
    }

    private FrameworkElement ReadOnlyFor(SettingsItem item)
        => new TextBlock
        {
            Text = SettingsBindings.ReadText(item.Id, _current) ?? string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
        };

    private FrameworkElement CustomFor(SettingsItem item) => item.Id switch
    {
        "store.backup" => BackupRow(),
        "store.usage" => StorageUsagePanel(),
        "about.onboarding" => OnboardingRow(),
        _ => new TextBlock(),
    };

    private FrameworkElement OnboardingRow()
    {
        var run = new Button
        {
            Content = "重新运行引导",
            Padding = new Thickness(10, 4, 10, 4),
            Cursor = Cursors.Hand,
        };
        run.Click += (_, _) =>
        {
            var wizard = new OnboardingWindow(_current, updated => { _apply(updated); })
            {
                Owner = this,
            };
            wizard.ShowDialog();
        };
        return run;
    }

    /// <summary>
    /// What Shiyu actually takes from the disk, measured where it lies —
    /// the number a user asking "这个小工具吃了我多少" is owed, with the
    /// folder one click away and a plain word when it grows past reason.
    /// </summary>
    private FrameworkElement StorageUsagePanel()
    {
        var panel = new StackPanel();

        TextBlock Row(string name, long bytes)
        {
            var row = new TextBlock
            {
                Text = $"{name}  {FormatBytes(bytes)}",
                Margin = new Thickness(0, 2, 0, 2),
                FontSize = (double)FindResource("Size.Secondary"),
            };
            row.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            return row;
        }

        void Measure()
        {
            panel.Children.Clear();

            var database = SizeOfFile(AppPaths.DatabaseFile)
                + SizeOfFile(AppPaths.DatabaseFile + "-wal")
                + SizeOfFile(AppPaths.DatabaseFile + "-shm");
            var images = SizeOfDirectory(AppPaths.ImageDirectory);

            panel.Children.Add(Row("数据库", database));
            panel.Children.Add(Row("图片原图", images));
            panel.Children.Add(Row("合计", database + images));

            // Past this, the folder is doing more than a tray tool should,
            // and the user deserves the number in the same breath as the why.
            const long threshold = 500L * 1024 * 1024;
            if (database + images > threshold)
            {
                var warning = new TextBlock
                {
                    Text = "已超过 500 MB——考虑缩短图片保留天数，或导出备份后清空。",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                    FontSize = (double)FindResource("Size.Caption"),
                };
                warning.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
                panel.Children.Add(warning);
            }
        }

        Measure();

        var open = new Button
        {
            Content = "打开数据文件夹",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 6, 0, 0),
            Cursor = Cursors.Hand,
        };
        open.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.DataDirectory)
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception)
            {
                // A folder that cannot be opened now is not worth a dialog.
            }
        };

        var refresh = new Button
        {
            Content = "刷新",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(8, 6, 0, 0),
            Cursor = Cursors.Hand,
        };
        refresh.Click += (_, _) => Measure();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(open);
        row.Children.Add(refresh);
        panel.Children.Add(row);

        return panel;
    }

    private static long SizeOfFile(string path)
        => File.Exists(path) ? new FileInfo(path).Length : 0;

    private static long SizeOfDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return total;
    }

    private static string FormatBytes(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / 1024.0 / 1024.0:0.#} MB"
            : $"{bytes / 1024.0:0.#} KB";

    private FrameworkElement BackupRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };

        var export = new Button { Content = "导出备份…", MinWidth = 96, Padding = new Thickness(10, 5, 10, 5), Cursor = Cursors.Hand };
        export.Click += (_, _) => _backup?.Export(this);

        var import = new Button { Content = "导入备份…", MinWidth = 96, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand };
        import.Click += (_, _) => _backup?.Import(this);

        row.Children.Add(export);
        row.Children.Add(import);
        return row;
    }

    private FrameworkElement WrapWithHint(FrameworkElement editor, string? hint)
    {
        if (hint is not { Length: > 0 })
        {
            return editor;
        }

        var stack = new StackPanel();
        editor.Margin = new Thickness(0, 0, 0, 2);
        stack.Children.Add(editor);

        var text = new TextBlock
        {
            Text = hint,
            TextWrapping = TextWrapping.Wrap,
            FontSize = (double)FindResource("Size.Caption"),
            Opacity = 0.75,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        stack.Children.Add(text);
        return stack;
    }

    private void UpdateSyncWarning()
    {
        if (_directoryBox is null || _directoryWarning is null)
        {
            return;
        }

        var folder = CloudSyncedPaths.DetectSyncFolder(_directoryBox.Text);
        _directoryWarning.Visibility = folder is null ? Visibility.Collapsed : Visibility.Visible;
        _directoryWarning.Text = folder is null
            ? string.Empty
            : $"⚠ 这个位置在「{folder}」里，会被同步到云端。历史记录并未加密，"
              + "放在这里等于把明文的剪贴板内容交给同步服务。";
    }

    // --- saving ------------------------------------------------------------------

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // Whatever a number box still holds uncommitted commits now, so the
        // save judges the value the user can see.
        foreach (var box in _numberBoxes.Values)
        {
            box.RaiseEvent(new RoutedEventArgs(LostFocusEvent, box));
        }

        var problems = new List<string>();

        var hotkeyNames = new Dictionary<string, string>
        {
            ["hotkey.capture"] = "划词翻译",
            ["hotkey.clipboard"] = "翻译剪贴板",
            ["hotkey.quickbar"] = "快速条",
            ["hotkey.bar"] = "窄条",
        };

        var parsed = new List<HotkeySpec?>();
        foreach (var (id, name) in hotkeyNames)
        {
            var spec = HotkeySpec.Parse(_edited[id].Text.Trim());
            if (spec is null)
            {
                problems.Add($"{name}的快捷键无法识别，需要形如 Ctrl+Shift+Z 且至少带一个修饰键。");
            }

            parsed.Add(spec);
        }

        foreach (var item in AllItems().Where(item => item.Control == SettingsControl.Number))
        {
            if (!int.TryParse(_edited[item.Id].Text.Trim(), out var value) || value < item.Min || value > item.Max)
            {
                problems.Add($"{item.Label}需要是 {(int)item.Min} 到 {(int)item.Max} 之间的整数。");
            }
        }

        var actions = ParseBarActions(_edited["bar.actions"].Text, problems);

        if (_edited["service.target-language"].Text.Trim().Length == 0)
        {
            problems.Add("译文语言不能为空。");
        }

        var chosen = parsed.Where(h => h is not null).ToList();
        if (chosen.Count == 4 && chosen.Distinct().Count() != 4)
        {
            // Registering the same combination twice means the second one
            // silently never works.
            problems.Add("四个快捷键不能相同。");
        }

        if (problems.Count > 0)
        {
            SaveStatus.Text = string.Join(" ", problems);
            return;
        }

        var directory = _edited["store.directory"].Text.Trim();
        if (CloudSyncedPaths.DetectSyncFolder(directory) is { } synced
            && !Confirm($"「{synced}」会被同步到云端，而历史记录并未加密。确定要把数据放在这里吗？"))
        {
            return;
        }

        var updated = _current;
        foreach (var item in AllItems().Where(item => item.Control
                     is SettingsControl.Segmented
                     or SettingsControl.Toggle
                     or SettingsControl.Number
                     or SettingsControl.Text
                     or SettingsControl.Password
                     or SettingsControl.Hotkey
                     or SettingsControl.Actions
                     or SettingsControl.Multiline
                     or SettingsControl.Directory))
        {
            var state = _edited[item.Id];
            updated = SettingsBindings.Apply(item.Id, updated, state.Text, state.Choice);
        }

        updated = updated with { BarActions = actions };

        var startupOk = StartupRegistration.Set(
            updated.StartWithWindows, Environment.ProcessPath ?? string.Empty);

        _apply(updated);

        SaveStatus.Text = startupOk
            ? "已保存。"
            : "设置已保存，但开机自启没能写入系统，请检查是否有安全软件拦截。";

        _secretBox?.Clear();
        if (_edited.TryGetValue("store.start-with-windows", out var startup))
        {
            // Follow whatever Windows ended up doing rather than leaving the
            // box asserting something untrue.
            startup.Toggle = StartupRegistration.IsEnabled();
            ApplyParentVisibility("store.start-with-windows", true);
        }
    }

    private static IEnumerable<SettingsItem> AllItems()
        => SettingsSchema.Tree.SelectMany(page => page.Sections).SelectMany(section => section.Items);

    /// <summary>
    /// Parses the comma-separated action list the user typed. Names rather
    /// than ids, because ids are for files and names are for people; anything
    /// unrecognised is a problem rather than a silent drop, because a
    /// silently-shrinking tray looks like a bug.
    /// </summary>
    private static List<string> ParseBarActions(string text, List<string> problems)
    {
        var byName = HoverActions.All.ToDictionary(HoverActions.Name, StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var raw in text.Split([',', '，', '、'], StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0)
            {
                continue;
            }

            if (byName.TryGetValue(raw, out var id))
            {
                if (!result.Contains(id))
                {
                    result.Add(id);
                }
            }
            else
            {
                problems.Add($"悬停动作「{raw}」无法识别。");
            }
        }

        if (result.Count == 0)
        {
            problems.Add("至少需要一个悬停动作。");
        }

        return result;
    }

    private bool Confirm(string message)
        => MessageBox.Show(
            this, message, "拾语", MessageBoxButton.OKCancel,
            MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
