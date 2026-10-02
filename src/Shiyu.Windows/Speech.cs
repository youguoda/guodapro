using System.Speech.Synthesis;
using System.Windows.Threading;

namespace Shiyu.Windows;

/// <summary>
/// SAPI 朗读服务（System.Speech）。
///
/// SpeechSynthesizer 是 COM 对象：诞生、选音色、说话、取消、释放都必须
/// 发生在同一条 STA 线程上。这里用一条专用线程持有它整个生命周期，UI
/// 线程只投递动作、绝不触碰对象本身——与 WinVHook 同一条纪律：回调只
/// 上报，工作在 owning 线程。合成器在第一次朗读时才创建：不朗读的用户
/// 不为一个静默的 COM 对象付线程开销。
/// </summary>
public sealed class SpeechSynthesis : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEvent _ready = new(false);
    private readonly Dictionary<string, string?> _voiceByPrefix = [];

    private Dispatcher? _dispatcher;
    private SpeechSynthesizer? _synthesizer;
    private bool _disposed;

    public SpeechSynthesis()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Shiyu 语音",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        // Run 在拿到 Dispatcher 之后才放行；WaitOne 的内存屏障让构造
        // 返回后 _dispatcher 一定可见。
        _ready.WaitOne();
    }

    /// <summary>
    /// 朗读；已在朗读则这次点击就是停止。判定在语音线程上原子完成，
    /// 调用方无需也无法可靠地跟踪朗读状态。
    /// </summary>
    /// <param name="text">要读的译文。</param>
    /// <param name="culturePrefix">
    /// 译文语言的 TwoLetterISOLanguageName（"zh"/"en"/…），据此挑音色；
    /// null 用默认音色。见 <see cref="Core.SpeechLanguage"/>。
    /// </param>
    public void SpeakOrStop(string text, string? culturePrefix)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Post(() =>
        {
            var synthesizer = EnsureSynthesizer();
            if (synthesizer is null)
            {
                return;
            }

            try
            {
                if (synthesizer.State == SynthesizerState.Speaking)
                {
                    synthesizer.SpeakAsyncCancelAll();
                    return;
                }

                SelectVoice(synthesizer, culturePrefix);
                synthesizer.SpeakAsync(text);
            }
            catch (Exception)
            {
                // expected: 朗读是锦上添花——没有声音设备、没有音色，
                // 都静默无效。
            }
        });
    }

    /// <summary>停止当前朗读（面板关闭时叫停，不留一个读完了没人听的语音）。</summary>
    public void Stop() => Post(() =>
    {
        try
        {
            _synthesizer?.SpeakAsyncCancelAll();
        }
        catch (Exception)
        {
            // expected: 停不下来的朗读随线程关停一起消失。
        }
    });

    private void Run()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _ready.Set();
        Dispatcher.Run();
    }

    private SpeechSynthesizer? EnsureSynthesizer()
    {
        if (_synthesizer is not null)
        {
            return _synthesizer;
        }

        try
        {
            _synthesizer = new SpeechSynthesizer();
            _synthesizer.SetOutputToDefaultAudioDevice();
        }
        catch (Exception)
        {
            // expected: 没有 SAPI 或没有音频输出——朗读按钮从此静默无效。
            _synthesizer = null;
        }

        return _synthesizer;
    }

    /// <summary>按语言前缀选音色，查过的前缀记住结论——枚举音色不便宜。</summary>
    private void SelectVoice(SpeechSynthesizer synthesizer, string? culturePrefix)
    {
        if (culturePrefix is null)
        {
            return;
        }

        if (_voiceByPrefix.TryGetValue(culturePrefix, out var cached))
        {
            if (cached is not null)
            {
                TrySelect(synthesizer, cached);
            }

            return;
        }

        string? match = null;
        try
        {
            match = synthesizer.GetInstalledVoices()
                .FirstOrDefault(voice => voice.Enabled && VoiceMatches(voice, culturePrefix))
                ?.VoiceInfo.Name;
        }
        catch (Exception)
        {
            // expected: 装了 SAPI 但音色枚举失败——按"没有匹配音色"记下，用默认。
        }

        _voiceByPrefix[culturePrefix] = match;
        if (match is not null)
        {
            TrySelect(synthesizer, match);
        }
    }

    private static bool VoiceMatches(InstalledVoice voice, string prefix)
    {
        try
        {
            return voice.VoiceInfo.Culture.TwoLetterISOLanguageName
                .StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // expected: 音色信息读不出——当作不匹配。
            return false;
        }
    }

    private static void TrySelect(SpeechSynthesizer synthesizer, string name)
    {
        try
        {
            synthesizer.SelectVoice(name);
        }
        catch (Exception)
        {
            // expected: 音色在这台机器上消失（拔掉的语音包）——留在默认音色上。
        }
    }

    /// <summary>把动作排到语音线程。关机后的迟到调用直接丢弃。</summary>
    private void Post(Action work)
    {
        var dispatcher = _dispatcher;
        if (_disposed || dispatcher is null)
        {
            return;
        }

        try
        {
            dispatcher.Invoke(work);
        }
        catch (Exception)
        {
            // expected: 线程已停（应用关机竞态）——朗读没有非完成不可的理由。
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _dispatcher?.Invoke(() =>
            {
                try
                {
                    _synthesizer?.SpeakAsyncCancelAll();
                    _synthesizer?.Dispose();
                }
                catch (Exception)
                {
                    // expected: 关机路径尽力而为——释放失败不再有后果。
                }

                _synthesizer = null;
            });

            _dispatcher?.BeginInvokeShutdown(DispatcherPriority.Background);
            _thread.Join(TimeSpan.FromSeconds(2));
        }
        finally
        {
            _ready.Dispose();
        }
    }
}
