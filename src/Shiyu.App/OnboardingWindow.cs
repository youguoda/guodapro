using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// The first-run guide. Its value is not a feature tour — it asks the few
/// things only the user knows: which app must never be recorded, what kinds
/// of copies belong in the history, and (optionally) the credential for
/// translation. Every step is skippable and the guide never returns on its
/// own; skipping leaves a fully working tool with sane defaults.
///
/// The items rendered here are leaves of the same settings tree the settings
/// window renders, built by the same editor factory — there is no second set
/// of definitions to keep in step.
/// </summary>
internal sealed class OnboardingWindow : Window
{
    private readonly AppSettings _current;
    private readonly Action<AppSettings> _apply;

    private readonly Dictionary<string, ItemState> _states = [];
    private readonly List<FrameworkElement> _steps = [];
    private int _step;

    private TextBlock _heading = new();
    private ContentControl _body = new();
    private TextBlock _stepLabel = new();
    private readonly Button _back = new() { Content = "上一步", Padding = new Thickness(12, 4, 12, 4), Cursor = Cursors.Hand };
    private readonly Button _next = new() { Content = "下一步", Padding = new Thickness(12, 4, 12, 4), Cursor = Cursors.Hand };

    public OnboardingWindow(AppSettings current, Action<AppSettings> apply)
    {
        _current = current;
        _apply = apply;

        Title = "欢迎使用拾语";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.FindResource("Brush.Background");
        FontFamily = (FontFamily)Application.Current.FindResource("Font.Ui");
        FontSize = (double)Application.Current.FindResource("Size.Body");

        BuildSteps();

        var root = new StackPanel { Margin = new Thickness(18) };

        _heading.FontSize = 18;
        _heading.FontWeight = FontWeights.SemiBold;
        _heading.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        root.Children.Add(_heading);

        var intro = new TextBlock
        {
            Text = "几步就绪，每一步都可跳过——跳过也完全可用。",
            Margin = new Thickness(0, 4, 0, 10),
            Opacity = 0.8,
        };
        intro.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        root.Children.Add(intro);

        _body.MinHeight = 150;
        root.Children.Add(_body);

        _stepLabel.Margin = new Thickness(0, 10, 0, 0);
        _stepLabel.FontSize = 13;
        _stepLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        root.Children.Add(_stepLabel);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        _back.Click += (_, _) => Show(_step - 1);
        _next.Click += (_, _) => Show(_step + 1);
        row.Children.Add(_back);
        row.Children.Add(_next);
        root.Children.Add(row);

        Content = root;
        Show(0);
    }

    private void BuildSteps()
    {
        _steps.Add(HotkeyStep());
        _steps.Add(ExclusionStep());
        _steps.Add(KindsStep());
        _steps.Add(CredentialStep());
    }

    /// <summary>An editor for one tree item, its state tracked for the finish.</summary>
    private FrameworkElement RowOf(string itemId, Func<SettingsItem, FrameworkElement> build)
    {
        var item = SettingsSchema.Find(itemId) ?? throw new ArgumentException(itemId);
        var state = new ItemState();
        _states[itemId] = state;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Margin = new Thickness(0, 0, 0, 8);

        var label = new TextBlock
        {
            Text = item.Label,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var editor = build(item);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    private FrameworkElement HotkeyStep()
    {
        var panel = new StackPanel();

        var note = new TextBlock
        {
            Text = "三个全局快捷键，现在确认或改成顺手的：",
            Margin = new Thickness(0, 0, 0, 8),
            Opacity = 0.8,
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        panel.Children.Add(note);

        var conflict = new TextBlock
        {
            Text = "⚠ 两个快捷键相同，第二个永远不会生效。",
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 13,
        };
        conflict.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");

        void RefreshConflict()
        {
            var parsed = new[] { "hotkey.capture", "hotkey.clipboard", "hotkey.bar" }
                .Select(id => _states.TryGetValue(id, out var state) ? HotkeySpec.Parse(state.Text.Trim()) : null)
                .Where(spec => spec is not null)
                .ToList();
            conflict.Visibility = parsed.Count != parsed.Distinct().Count()
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        foreach (var id in new[] { "hotkey.capture", "hotkey.clipboard", "hotkey.bar" })
        {
            var captured = id;
            var row = RowOf(id, item =>
            {
                var editor = ItemEditors.Text(item, _current, _states[captured]);
                editor.PreviewKeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        RefreshConflict();
                        e.Handled = true;
                    }
                };
                return editor;
            });
            panel.Children.Add(row);
        }

        panel.Children.Add(conflict);
        return panel;
    }

    private FrameworkElement ExclusionStep()
    {
        var panel = new StackPanel();

        var state = new ItemState
        {
            Text = SettingsBindings.ReadText("exclusions", _current) ?? string.Empty,
        };
        _states["exclusions"] = state;

        var ask = new TextBlock
        {
            Text = "密码管理器等敏感来源永远不记录。常见工具已内置排除；如果你装的不在下面，勾选或直接填名字：",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Opacity = 0.8,
        };
        ask.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        panel.Children.Add(ask);

        var presetNames = ExclusionPolicy.Presets
            .Select(rule => rule.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = new List<string>(presetNames);
        foreach (var process in Process.GetProcesses())
        {
            var name = process.ProcessName;
            if (name.Length > 0
                && (name.Contains("pass", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("vault", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("keepass", StringComparison.OrdinalIgnoreCase))
                && !candidates.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(name);
            }
        }

        var picks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new StackPanel();
        foreach (var name in candidates.Take(24))
        {
            var captured = name;
            var box = new CheckBox
            {
                Content = name,
                IsChecked = presetNames.Contains(name),
                IsEnabled = !presetNames.Contains(name),
                Margin = new Thickness(0, 1, 12, 1),
                Cursor = Cursors.Hand,
                ToolTip = presetNames.Contains(name) ? "已内置排除" : "勾选后不再记录来自它的复制",
            };
            box.Checked += (_, _) => picks.Add(captured);
            box.Unchecked += (_, _) => picks.Remove(captured);
            list.Children.Add(box);
        }

        var listHost = new ScrollViewer
        {
            Content = list,
            MaxHeight = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(listHost);

        var manual = new TextBox
        {
            Padding = new Thickness(4),
            ToolTip = "应用名，回车加入；留空跳过",
        };
        manual.SetResourceReference(TextBox.BackgroundProperty, "Brush.SurfaceInput");
        var addRow = new StackPanel { Orientation = Orientation.Horizontal };
        addRow.Children.Add(manual);

        var add = new Button { Content = "加入", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0), Cursor = Cursors.Hand };
        add.Click += (_, _) =>
        {
            var name = manual.Text.Trim();
            if (name.Length > 0)
            {
                picks.Add(name);
                manual.Clear();
            }
        };
        addRow.Children.Add(add);
        panel.Children.Add(addRow);
        panel.Children.Add(new TextBlock
        {
            Text = "勾选与手填的名字都会写进设置的排除规则，随时可改。",
            FontSize = 13,
            Opacity = 0.7,
            Margin = new Thickness(0, 6, 0, 0),
        });

        // The picks ride in the panel's tag, folded into the state on leave.
        panel.Tag = picks;
        return panel;
    }

    private FrameworkElement KindsStep()
    {
        var panel = new StackPanel();

        var note = new TextBlock
        {
            Text = "要记录哪些复制？文本始终记录——没有它拾语就不成立。",
            Margin = new Thickness(0, 0, 0, 8),
            Opacity = 0.8,
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        panel.Children.Add(note);

        panel.Children.Add(RowOf("record.images", item => ItemEditors.Toggle(item, _current, _states["record.images"])));
        panel.Children.Add(RowOf("record.files", item => ItemEditors.Toggle(item, _current, _states["record.files"])));
        return panel;
    }

    private FrameworkElement CredentialStep()
    {
        var panel = new StackPanel();

        var note = new TextBlock
        {
            Text = "划词翻译需要模型服务的凭据——不想用就跳过这步，其余功能不受影响。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Opacity = 0.8,
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        panel.Children.Add(note);

        panel.Children.Add(RowOf("service.base-url", item => ItemEditors.Text(item, _current, _states["service.base-url"])));
        panel.Children.Add(RowOf("service.model", item => ItemEditors.Text(item, _current, _states["service.model"])));
        panel.Children.Add(RowOf("service.api-key", item => ItemEditors.Password(item, _current, _states["service.api-key"]).Editor));
        return panel;
    }

    private static string JoinExclusions(IReadOnlyCollection<string> names)
        => names.Count == 0 ? string.Empty : string.Join(Environment.NewLine, names);

    private void Show(int index)
    {
        if (index < 0)
        {
            return;
        }

        if (index >= _steps.Count)
        {
            Finish();
            return;
        }

        // Leaving the exclusion step folds its picks into the state.
        if (_steps[_step] is StackPanel { Tag: HashSet<string> picks } leaving
            && _states.TryGetValue("exclusions", out var exclusions))
        {
            var preset = ExclusionPolicy.Presets.Select(rule => rule.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var chosen = picks.Where(name => !preset.Contains(name)).ToList();
            var typed = exclusions.Text
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !preset.Contains(line) && !chosen.Contains(line, StringComparer.OrdinalIgnoreCase))
                .ToList();
            typed.AddRange(chosen);
            exclusions.Text = JoinExclusions(typed);
        }

        _step = index;
        _heading.Text = index switch
        {
            0 => "快捷键",
            1 => "不记录什么",
            2 => "记录什么",
            _ => "翻译服务（可选）",
        };
        _body.Content = _steps[index];
        _stepLabel.Text = $"第 {index + 1} 步，共 {_steps.Count} 步";
        _back.Visibility = index == 0 ? Visibility.Collapsed : Visibility.Visible;
        _next.Content = index == _steps.Count - 1 ? "完成" : "下一步";
    }

    private void Finish()
    {
        // Hotkeys that do not parse, or collide, fall back to what already
        // works: skipping a step must never leave the tool worse than its
        // defaults.
        var hotkeyIds = new[] { "hotkey.capture", "hotkey.clipboard", "hotkey.bar" };
        var parsed = hotkeyIds
            .Select(id => _states.TryGetValue(id, out var state) ? HotkeySpec.Parse(state.Text.Trim()) : null)
            .ToList();
        if (parsed.Any(spec => spec is null) || parsed.Distinct().Count() != parsed.Count)
        {
            foreach (var id in hotkeyIds.Where(id => _states.TryGetValue(id, out _)))
            {
                _states[id].Text = SettingsBindings.ReadText(id, _current) ?? string.Empty;
            }
        }

        // Untouched states keep their current values: the apply chain runs
        // over every wizard item with whatever the user left in them.
        var updated = _current;
        foreach (var (id, state) in _states)
        {
            updated = SettingsBindings.Apply(id, updated, state.Text, state.Choice);
        }

        _apply(updated with { OnboardingCompleted = true });
        Close();
    }
}
