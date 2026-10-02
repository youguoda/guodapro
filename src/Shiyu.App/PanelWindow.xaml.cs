using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Shiyu.Core;
using Shiyu.Windows;

namespace Shiyu.App;

/// <summary>词典卡一个义项的展示形状：Core 的卡加上一条拼好的同义词行。</summary>
public sealed record SenseView(
    string? PartOfSpeech,
    IReadOnlyList<string> Definitions,
    IReadOnlyList<string> Examples,
    string? SynonymsLine);

/// <summary>
/// The translation panel: the original above, the translation growing beneath
/// it as the words arrive.
///
/// Beyond the stream it carries three quiet additions (ticket 35): a dictionary
/// card that appears when the selection was a single word, a sentence-by-sentence
/// alignment toggle, and read-aloud of the finished translation. All of them are
/// best-effort layers over the translation — the translation itself never waits
/// for any of them.
///
/// Like the badge, it never takes focus. That leaves it unable to receive key
/// presses, so Escape is a global hotkey held only while the panel is on
/// screen — see <see cref="_escape"/>.
/// </summary>
public partial class PanelWindow : Window
{
    /// <summary>
    /// 当前热键注册表的取用口，而非一次性捕获（O-43）：注册表随每次设置
    /// 保存整体重建（O-20），攥着退役实例的窗口再按 Esc 只会悄悄失灵。
    /// </summary>
    private readonly Func<HotkeyRegistry> _hotkeys;
    private readonly WindowsClipboardWriter _clipboard;
    private readonly Func<ITranslationBackend> _backend;
    private readonly Action<string, string>? _saveTranslation;

    /// <summary>按选中文本现造词典端口；null 表示这段文本不吃词典卡。</summary>
    private readonly Func<string, IDictionaryApi?>? _dictionary;

    private readonly SpeechSynthesis? _speech;

    /// <summary>翻译此刻是否有一条能走的路；null 表示"不归我管"（测试与旧调用）。</summary>
    private readonly Func<bool>? _backendReady;

    /// <summary>「去配置」深链：打开设置的服务页，面板自身让开。</summary>
    private readonly Action? _openSettings;

    private IDisposable? _escape;
    private CancellationTokenSource? _inFlight;
    private TranslationSession? _session;
    private string _original = string.Empty;
    private string _target;
    private string? _source;

    /// <summary>逐句对照显示开关。默认关——整段流式是主路径，对照是阅读辅助。</summary>
    private bool _sentenceMode;

    /// <summary>词典卡换代号：新一次翻译自增，迟到的卡据此知道自己过时了。</summary>
    private int _cardRun;

    public PanelWindow(
        Func<HotkeyRegistry> hotkeys,
        WindowsClipboardWriter clipboard,
        Func<ITranslationBackend> backend,
        AppSettings settings,
        Action<string, string>? saveTranslation = null,
        Func<string, IDictionaryApi?>? dictionary = null,
        SpeechSynthesis? speech = null,
        Func<bool>? backendReady = null,
        Action? openSettings = null)
    {
        InitializeComponent();

        _hotkeys = hotkeys;
        _clipboard = clipboard;
        _backend = backend;
        _saveTranslation = saveTranslation;
        _dictionary = dictionary;
        _speech = speech;
        _backendReady = backendReady;
        _openSettings = openSettings;
        _target = settings.TargetLanguage;
        _source = settings.SourceLanguage;

        Backdrop.AttachShell(this, Shell, () => BackdropKind.Acrylic);
    }

    /// <summary>
    /// 翻译服务是否已配置。公共通道未上线时不构成可用的路（票 08）——
    /// 热键、划词、复制徽标三条路都汇到这里，未配置的结局只有引导卡。
    /// </summary>
    private bool TranslationReady => _backendReady?.Invoke() != false;

    /// <summary>
    /// 译文语言跟随设置即时换新（O-20）：面板是复用实例，改设置不该等
    /// 重启——下一次翻译就用新语言，正在流式中的那一次按它开始时的语言走完。
    /// </summary>
    public void ApplySettings(AppSettings settings)
    {
        _target = settings.TargetLanguage;
        _source = settings.SourceLanguage;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TransientWindow.MakeNonActivating(new WindowInteropHelper(this).Handle, ZBand.Topmost);
    }

    /// <summary>
    /// Shows the panel beside the cursor and starts translating.
    ///
    /// <paramref name="onDisplayed"/> fires exactly when the panel is on screen
    /// (after placement, before the stream starts) — 划词路径靠它钉住"剪贴板
    /// 还原放在翻译面板显示之后"的次序（票 37 的 Glossy 细节）。
    /// </summary>
    public async Task TranslateAsync(string text, Action? onDisplayed = null)
    {
        _original = text;
        OriginalText.Text = text;
        TranslatedText.Text = string.Empty;
        SentencePairs.ItemsSource = null;
        StatusText.Visibility = Visibility.Collapsed;
        SaveButton.Content = "存入历史";
        UpdateDirectionLabel();

        // 新的选中文本作废旧卡：卡还在路上的话，到岸后发现换代号变了就不上屏。
        _cardRun++;
        DictionaryArea.Visibility = Visibility.Collapsed;
        SetupCard.Visibility = Visibility.Collapsed;
        TranslationScroll.Visibility = Visibility.Visible;

        if (!IsVisible)
        {
            Show();
        }

        // Full opacity immediately, no fade-in: a layered window fading in
        // from transparency never composites (ticket 04's badge finding), so
        // the panel would rely on the translation stream's layout churn to
        // rescue it — arriving late or, with a fast backend, not at all.
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        UpdateLayout();
        MoveBesideCursor();

        // 面板已显示：此刻之后的剪贴板写不再挡在用户和首帧之间。
        onDisplayed?.Invoke();

        // The entrance lives on the content, not the window: same rule,
        // opposite side — the sheet fades and rises, the window stays solid.

        HoldEscape();

        // 未配置翻译服务：给一张引导卡而不是异常文本（票 08 的验收线——
        // 任何路径都不出现异常英文）。词典卡也不发：没有自己的密钥，
        // LLM 词典路本来就缺席。
        if (!TranslationReady)
        {
            ShowSetupCard();
            return;
        }

        // 两阶段词典（票 35）：阶段一翻译照常跑完上屏；阶段二在它之后补卡。
        // 查询与翻译并行发出（省一轮往返），但渲染严格排在翻译之后——
        // 用户先看到译文，再看到词典细节，顺序即"两阶段"的含义。
        var cardRun = _cardRun;
        var cardTask = FetchDictionaryCard(text);
        await RunTranslation();
        await RenderCardWhenCurrent(cardTask, cardRun);
    }

    /// <summary>引导卡：告诉用户去哪，而不是报一个错。</summary>
    private void ShowSetupCard()
    {
        TranslatedText.Text = string.Empty;
        SentencePairs.ItemsSource = null;
        TranslationScroll.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Collapsed;
        SaveButton.IsEnabled = false;
        SetupCard.Visibility = Visibility.Visible;
    }

    private void OnOpenSetup(object sender, RoutedEventArgs e)
    {
        _openSettings?.Invoke();

        // 设置窗要落到焦点上，无激活的面板留在原地只会挡视线。
        Dismiss();
    }

    private async Task RunTranslation()
    {
        // 换方向也会走到这里：未配置的结局同样是引导卡，不是异常文本。
        if (!TranslationReady)
        {
            ShowSetupCard();
            return;
        }

        // A second request while the first is still arriving abandons it; the
        // user has moved on and the old stream's text would interleave.
        _inFlight?.Cancel();
        _inFlight?.Dispose();
        _inFlight = new CancellationTokenSource();

        var session = new TranslationSession(_backend());
        _session = session;

        session.Updated += () => Dispatcher.Invoke(() =>
        {
            if (!ReferenceEquals(_session, session))
            {
                return;
            }

            TranslatedText.Text = session.Text;
            if (_sentenceMode)
            {
                RenderSentencePairs(session.Text);
            }

            // The label follows the request actually on the wire: an echo
            // retry swaps the direction under the user, and the label must
            // not keep claiming the direction that just failed.
            UpdateDirectionLabel(session.CurrentRequest);

            // A stream reads like a conversation: follow the newest line
            // unless the user scrolled up to re-read.
            if (TranslationScroll.ScrollableHeight > 0
                && TranslationScroll.VerticalOffset >= TranslationScroll.ScrollableHeight - 24)
            {
                TranslationScroll.ScrollToEnd();
            }

            if (session.State == TranslationState.Failed && session.Error is { } error)
            {
                // The partial text stays on screen beneath the error: two
                // thirds of a translation is still two thirds of what was
                // wanted, and taking it away would be a second failure.
                StatusText.Text = error;
                StatusText.Visibility = Visibility.Visible;
            }

            UpdateLayout();
        });

        await session.RunAsync(
            new TranslationRequest(_original, _target) { SourceLanguage = _source },
            _inFlight.Token);

        // Kept work needs a door: only a finished translation is worth saving,
        // and the button says what it will do with it.
        SaveButton.IsEnabled = _saveTranslation is not null
            && session.State == TranslationState.Finished
            && session.Text.Trim().Length > 0;
    }

    /// <summary>
    /// 阶段二的取卡：单词判定通过才发查。慢路（LLM 词典化）与快路（600ms
    /// 预算的免费词典）都由 <see cref="_dictionary"/> 决定；任何失败都只是
    /// 没有卡。只取不渲染——渲染时机归 <see cref="RenderCardWhenCurrent"/>。
    /// </summary>
    private async Task<DictionaryCard?> FetchDictionaryCard(string text)
    {
        if (_dictionary is null)
        {
            return null;
        }

        var word = text.Trim();
        IDictionaryApi? api;
        try
        {
            api = _dictionary(word);
        }
        catch (Exception)
        {
            // expected: 组装端口失败与查词失败同类：静默没有卡。
            return null;
        }

        if (api is null)
        {
            return null;
        }

        try
        {
            return await api.LookupAsync(DictionaryWord.LookupKey(word), CancellationToken.None);
        }
        catch (Exception)
        {
            // expected: 端口契约本不该抛；抛了也一样是"没有卡"。
            return null;
        }
        finally
        {
            (api as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// 翻译之后补卡：迟到的换代检查保证只有最新一次翻译的卡会上屏——
    /// 翻译失败也补（卡只关于原文的那个词，与译文成败无关）。
    /// </summary>
    private async Task RenderCardWhenCurrent(Task<DictionaryCard?> fetch, int run)
    {
        DictionaryCard? card;
        try
        {
            card = await fetch;
        }
        catch (Exception)
        {
            // expected: 换代竞态或取卡失败——没有卡上屏，翻译不受影响。
            card = null;
        }

        if (card is null || run != _cardRun)
        {
            return;
        }

        try
        {
            Dispatcher.Invoke(() =>
            {
                if (run != _cardRun)
                {
                    return;
                }

                RenderCard(card);
            });
        }
        catch (Exception)
        {
            // expected: 应用关停的竞态里 Invoke 会抛——一张迟到的卡
            // 不值得带崩进程。
        }
    }

    private void RenderCard(DictionaryCard card)
    {
        CardWord.Text = card.Word;
        CardPhonetic.Text = card.Phonetic ?? string.Empty;
        CardSenses.ItemsSource = card.Senses.Select(sense => new SenseView(
            sense.PartOfSpeech,
            sense.Definitions,
            sense.Examples,
            sense.Synonyms.Count > 0 ? "同义词：" + string.Join("、", sense.Synonyms) : null));
        DictionaryArea.Visibility = Visibility.Visible;
        UpdateLayout();
    }

    private void OnToggleSentenceAlignment(object sender, RoutedEventArgs e)
    {
        _sentenceMode = !_sentenceMode;
        ApplySentenceMode();
    }

    /// <summary>开关只切显示：对照数据由同一份流式文本现算，切换零成本、随时可翻。</summary>
    private void ApplySentenceMode()
    {
        SentenceToggle.FontWeight = _sentenceMode
            ? FontWeights.SemiBold
            : FontWeights.Normal;
        TranslatedText.Visibility = _sentenceMode ? Visibility.Collapsed : Visibility.Visible;
        SentencePairs.Visibility = _sentenceMode ? Visibility.Visible : Visibility.Collapsed;

        if (_sentenceMode)
        {
            RenderSentencePairs(_session?.Text ?? string.Empty);
        }
    }

    private void RenderSentencePairs(string translated)
        => SentencePairs.ItemsSource = SentenceAlign.Pair(_original, translated);

    private void OnSpeak(object sender, RoutedEventArgs e)
    {
        if (_speech is null || _session?.Text.Trim() is not { Length: > 0 } text)
        {
            return;
        }

        // 读的是当前屏上的译文，用的语言也随它——回声换向后读的应该是
        // 换向后的那段，方向标签看到的是哪个，耳朵听到的就是哪个。
        var language = _session.CurrentRequest?.TargetLanguage ?? _target;
        _speech.SpeakOrStop(text, SpeechLanguage.CulturePrefix(language));
    }

    /// <summary>
    /// Hands the finished pair to whoever owns history. The link to the
    /// original entry is resolved there — here there is only text.
    /// </summary>
    private void OnSaveToHistory(object sender, RoutedEventArgs e)
    {
        if (_saveTranslation is null || _session?.Text.Trim() is not { Length: > 0 } translated)
        {
            return;
        }

        _saveTranslation(_original, translated);
        SaveButton.IsEnabled = false;
        SaveButton.Content = "✓ 已存入";
    }

    private void MoveBesideCursor()
    {
        var cursor = ScreenGeometry.CursorPosition();
        var workArea = ScreenGeometry.WorkAreaAt(cursor);

        var source = PresentationSource.FromVisual(this);
        var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        var placed = BadgePlacement.Place(
            cursor,
            (int)Math.Ceiling(ActualWidth * scaleX),
            (int)Math.Ceiling(ActualHeight * scaleY),
            workArea);

        // A global surface: the translation panel belongs to the copy, not to
        // the bar, and keeps the topmost band wherever the bar sits.
        TransientWindow.MoveTo(new WindowInteropHelper(this).Handle, placed, ZBand.Topmost);
    }

    /// <summary>
    /// Takes Escape for as long as the panel is up. Releasing it reliably
    /// matters more than taking it: an Escape left registered would be
    /// swallowed system-wide.
    /// </summary>
    private void HoldEscape()
    {
        _escape ??= _hotkeys().TryRegisterScoped(
            new Hotkey(HotkeyModifiers.None, 0x1B, "关闭面板"),
            () => Dispatcher.Invoke(Dismiss));

        // Escape being unavailable costs the user a click on the close button;
        // it is not worth refusing to show a translation over.
        HintText.Text = _escape is null ? "点 ✕ 关闭" : "Esc 关闭";
    }

    private void ReleaseEscape()
    {
        _escape?.Dispose();
        _escape = null;
    }

    private void Dismiss()
    {
        _inFlight?.Cancel();
        ReleaseEscape();

        // 面板没了，朗读也该停：读完一个没人看的译文是对接下来的打扰。
        _speech?.Stop();

        // Instant hide. A fade here reads as jank on a layered window — the
        // text drops out before the tinted sheet does (the user's own words:
        // "先没有字体，再一个灰板") — and the system's Win+V panel, the
        // benchmark for this exact surface, also pops shut with no exit.
        Hide();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Dismiss();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_session is null || _session.Text.Length == 0)
        {
            return;
        }

        CopyButton.Content = _clipboard.SetText(_session.Text) ? "已复制" : "复制失败";
    }

    private async void OnSwapDirection(object sender, RoutedEventArgs e)
    {
        // async void 逃出去的异常是进程级崩溃（O-05）。翻译失败本身已被
        // 会话收敛成状态文字；这里的 catch 罩的是换向组装路径上的意外。
        try
        {
            // Swapping only makes sense between two named languages; with the
            // source left to the backend there is nothing to swap it with.
            (_source, _target) = (_target, _source ?? "English");
            UpdateDirectionLabel();
            CopyButton.Content = "复制译文";
            await RunTranslation();
        }
        catch (Exception failure)
        {
            Log.Event(LogEvent.TranslationFailed, failure, ("swap", 1));
            StatusText.Text = "翻译失败：" + failure.Message;
            StatusText.Visibility = Visibility.Visible;
        }
    }

    private void UpdateDirectionLabel(TranslationRequest? attempt = null)
    {
        // 源语言未声明时用本地文字系统先验代替"自动识别"占位——纯码位
        // 统计，不产生请求，流式照旧。猜不出（纯数字之类）才退回占位。
        var request = attempt ?? new TranslationRequest(_original, _target)
        {
            SourceLanguage = _source,
        };
        var source = request.SourceLanguage
            ?? LanguageGuess.FromText(_original).Label
            ?? "自动识别";

        DirectionLabel.Text = $"{source} → {request.TargetLanguage}";
    }

    /// <summary>Lets the application close it for real on shutdown.</summary>
    public void CloseForGood()
    {
        _inFlight?.Cancel();
        ReleaseEscape();
        _speech?.Stop();
        Close();
    }
}
