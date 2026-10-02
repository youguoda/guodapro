using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shiyu.Core;

namespace Shiyu.App;

/// <summary>
/// 服务页与首次引导共用的「服务商预设」行（票 08）：预设下拉（默认四家、
/// 「更多」里的 Kimi、以及「自定义」）、「申请密钥」直达服务商、「测试
/// 连接」三态人话。设置窗与引导窗渲染同一棵树、同一批编辑器——这一行
/// 也只有这一份定义，谁也不许抄走。
///
/// <paramref name="state"/>.Text 承载当前预设 Id（空串=自定义），随保存
/// 走 <see cref="SettingsBindings.Apply"/>；地址与模型由各自的行自己写，
/// 这里只负责选中即代填、手改即降级为自定义（数据层另有
/// <see cref="ProviderPresets.ResolveFor"/> 兜底）。
/// </summary>
internal sealed class ServicePresetRow
{
    private const string CustomTag = "";

    private readonly ItemState _state;
    private readonly Func<(string BaseUrl, string Model, string ApiKey)> _readForm;
    private readonly Action<ProviderPreset> _applyPreset;

    private readonly ComboBox _picker = new() { MinWidth = 230, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _keyLink = new()
    {
        Content = "申请密钥 ↗",
        Padding = new Thickness(10, 3, 10, 3),
        Margin = new Thickness(8, 0, 0, 0),
        Cursor = Cursors.Hand,
        Visibility = Visibility.Collapsed,
        ToolTip = "到服务商的控制台申请一个 API 密钥（在浏览器里打开）",
    };
    private readonly Button _test = new()
    {
        Content = "测试连接",
        Padding = new Thickness(10, 3, 10, 3),
        Cursor = Cursors.Hand,
        ToolTip = "发一条极小的真实请求，验证地址、密钥与模型",
    };
    private readonly TextBlock _result = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0),
        Visibility = Visibility.Collapsed,
    };

    private bool _syncing;

    public FrameworkElement Element { get; }

    public ServicePresetRow(
        AppSettings baseline,
        ItemState state,
        Func<(string BaseUrl, string Model, string ApiKey)> readForm,
        Action<ProviderPreset> applyPreset)
    {
        _state = state;
        _readForm = readForm;
        _applyPreset = applyPreset;

        BuildPicker(baseline);
        _keyLink.Click += (_, _) => OpenKeyUrl();
        _test.Click += async (_, _) => await TestConnectionAsync();
        Element = Build();
    }

    private FrameworkElement Build()
    {
        var pickRow = new StackPanel { Orientation = Orientation.Horizontal };
        pickRow.Children.Add(_picker);
        pickRow.Children.Add(_keyLink);

        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        actionRow.Children.Add(_test);

        var panel = new StackPanel();
        panel.Children.Add(pickRow);
        panel.Children.Add(actionRow);
        panel.Children.Add(_result);
        return panel;
    }

    private void BuildPicker(AppSettings baseline)
    {
        _picker.Items.Add(Header("常用"));
        foreach (var preset in ProviderPresets.Primary)
        {
            _picker.Items.Add(Option(preset));
        }

        if (ProviderPresets.More.Count > 0)
        {
            _picker.Items.Add(Header("更多"));
            foreach (var preset in ProviderPresets.More)
            {
                _picker.Items.Add(Option(preset));
            }
        }

        _picker.Items.Add(new ComboBoxItem { Content = "自定义", Tag = CustomTag });

        _picker.SelectionChanged += OnPicked;

        // 初选按地址与模型的现状反查，而不是文件里的 Id：手改过的配置
        // 打开窗就该看见「自定义」，别拿一个失效的预设名糊弄人。
        _syncing = true;
        var current = Match(baseline.BackendBaseUrl, baseline.BackendModel);
        _state.Text = current?.Id ?? CustomTag;
        SelectByTag(current?.Id ?? CustomTag);
        _keyLink.Visibility = current is null ? Visibility.Collapsed : Visibility.Visible;
        _syncing = false;

        static ComboBoxItem Option(ProviderPreset preset) => new()
        {
            Content = preset.DisplayName,
            Tag = preset.Id,
            ToolTip = $"{preset.BaseUrl} · {preset.DefaultModel}",
        };

        static ComboBoxItem Header(string text) => new()
        {
            Content = text,
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold,
        };
    }

    /// <summary>地址或模型被改动后由宿主窗口调来：与所选预设不符就降级为「自定义」。</summary>
    public void NoteAddressEdited()
    {
        if (_syncing || _state.Text.Length == 0)
        {
            return;
        }

        var preset = ProviderPresets.Find(_state.Text);
        var (baseUrl, model, _) = _readForm();
        if (preset is null
            || baseUrl.TrimEnd('/') != preset.BaseUrl
            || (model.Trim() != preset.DefaultModel
                && !preset.AltModels.Contains(model.Trim(), StringComparer.Ordinal)))
        {
            DemoteToCustom();
        }
    }

    private void OnPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || e.AddedItems.Count == 0)
        {
            return;
        }

        if (e.AddedItems[0] is not ComboBoxItem { IsEnabled: true } item
            || item.Tag is not string id)
        {
            return;
        }

        if (id.Length == 0)
        {
            _state.Text = CustomTag;
            _keyLink.Visibility = Visibility.Collapsed;
            return;
        }

        if (ProviderPresets.Find(id) is not { } preset)
        {
            return;
        }

        _state.Text = id;
        _keyLink.Visibility = Visibility.Visible;
        _applyPreset(preset);
    }

    private void DemoteToCustom()
    {
        _syncing = true;
        _state.Text = CustomTag;
        SelectByTag(CustomTag);
        _keyLink.Visibility = Visibility.Collapsed;
        _syncing = false;
    }

    private void SelectByTag(string tag)
    {
        foreach (var item in _picker.Items.OfType<ComboBoxItem>()
                     .Where(item => item.IsEnabled && item.Tag is string))
        {
            if ((string)item.Tag == tag)
            {
                _picker.SelectedItem = item;
                return;
            }
        }
    }

    private static ProviderPreset? Match(string baseUrl, string model)
        => ProviderPresets.All.FirstOrDefault(preset =>
            baseUrl.TrimEnd('/') == preset.BaseUrl
            && (model.Trim() == preset.DefaultModel
                || preset.AltModels.Contains(model.Trim(), StringComparer.Ordinal)));

    private void OpenKeyUrl()
    {
        if (ProviderPresets.Find(_state.Text) is not { } preset)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(preset.ApiKeyUrl) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // 打不开浏览器不该静默——这行链接是配置流程的一部分。
            ShowResult("打不开浏览器，请手动访问服务商的控制台申请密钥。", "Brush.TextSecondary");
        }
    }

    private async Task TestConnectionAsync()
    {
        var (baseUrl, model, apiKey) = _readForm();
        if (string.IsNullOrWhiteSpace(baseUrl)
            || string.IsNullOrWhiteSpace(model)
            || string.IsNullOrWhiteSpace(apiKey))
        {
            ShowResult("请先填好服务地址、模型与密钥，再测试连接。", "Brush.TextSecondary");
            return;
        }

        _test.IsEnabled = false;
        ShowResult("正在测试……", "Brush.TextSecondary");

        // 与真翻译同一条路：预设的附加字段与温度规则一并生效（票 08）。
        // 密钥只进 Authorization 头，不进结果文案。默认首字节 15 秒恰好是
        // 测试连接想要的等待上限（O-23 之后不再有总时长语义）。
        var outcome = await ConnectionProbe.TestAsync(
            new TranslationBackendOptions(baseUrl.Trim(), model.Trim(), apiKey),
            ProviderPresets.Find(_state.Text),
            httpClient: HttpClients.Shared);

        _test.IsEnabled = true;
        ShowResult(
            Describe(outcome),
            outcome.Verdict switch
            {
                ConnectionTestVerdict.Success => "Brush.Accent",
                ConnectionTestVerdict.InvalidKey => "Brush.Danger",
                _ => "Brush.Danger",
            });
    }

    private static string Describe(ConnectionTestOutcome outcome)
    {
        if (outcome.Message is { Length: > 0 })
        {
            // 百炼免费额度耗尽之类的"成功但有话要说"。
            return outcome.Verdict == ConnectionTestVerdict.Success
                ? $"连接成功（{(int)outcome.Elapsed!.Value.TotalMilliseconds:0} ms）。{outcome.Message}"
                : outcome.Message;
        }

        return outcome.Verdict == ConnectionTestVerdict.Success
            ? $"连接成功（{(int)outcome.Elapsed!.Value.TotalMilliseconds:0} ms），配置可用。"
            : "测试失败。";
    }

    private void ShowResult(string text, string brush)
    {
        _result.Text = text;
        _result.Visibility = Visibility.Visible;
        _result.SetResourceReference(TextBlock.ForegroundProperty, brush);
    }
}
