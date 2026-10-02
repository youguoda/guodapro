namespace Shiyu.Core;

/// <summary>
/// 设置保存失败的专用异常（O-20）。用专用类型而不是让 IOException
/// 裸逃，是因为两条边界都要靠它守住：
/// 调用方能精确地把它和别的 IO 噪音分开，翻成一句"保存失败"；
/// 而抛出它本身就证明 store 拒绝了这次改动——内存与磁盘仍然一致，
/// 绝没有"内存已改、磁盘没写"的中间态。
/// </summary>
public sealed class SettingsSaveException : Exception
{
    public SettingsSaveException(string path, Exception inner)
        : base($"设置没能写入磁盘（{Path.GetFileName(path)}），本次改动没有生效。", inner)
    {
    }
}

/// <summary>
/// <see cref="SettingsStore.Load(string, Func{AppSettings, AppSettings}?)"/> 的结果：
/// store 本身，加上"加载时发生了什么"。<see cref="QuarantinedPath"/> 非 null
/// 表示原文件解析不了、已被改名保留——App 借此向用户说明一次，而不是
/// 无声地换上默认设置。
/// </summary>
public sealed record SettingsLoadResult(SettingsStore Store, string? QuarantinedPath);

/// <summary>
/// 设置的唯一写入口（O-20/O-07 的根治）。此前每个写入方手里都攥着一份
/// 完整快照整份写回，最后保存的一方获胜，别处改的字段被旧值悄悄抹掉；
/// 现在所有写入都是"在最新值上做增量"：mutate 拿到的一定是磁盘真值，
/// 写盘成功后 Current 才前进，随后广播 <see cref="Changed"/>。
///
/// 一处刻意的例外：环境变量旁路（如 SHIYU_RELAY_URL）叠加在
/// <see cref="Current"/> 上供一切读取，但从不落盘、也从不出现在 mutate
/// 面前——探针改道不该被第一次随手保存写进用户的设置文件。
/// </summary>
public sealed class SettingsStore
{
    private readonly object _gate = new();
    private readonly Func<AppSettings, AppSettings>? _bypass;

    /// <summary>磁盘真值：mutate 看到的、Save 写下的，就是它。</summary>
    private AppSettings _disk;

    /// <summary>叠加旁路后的值：Current 对外给出的。缓存而非每次现算，Current 的读频繁而旁路几乎不变。</summary>
    private AppSettings _effective;

    /// <summary>当前生效的设置（磁盘真值 + 旁路叠加）。</summary>
    public AppSettings Current
    {
        get { lock (_gate) { return _effective; } }
    }

    /// <summary>
    /// 每次成功写入后广播最新生效值。在锁外触发：处理器里若再调
    /// <see cref="Update"/> 不该死锁，而读 <see cref="Current"/> 本来就另有锁。
    /// </summary>
    public event Action<AppSettings>? Changed;

    /// <param name="bypass">非持久化旁路：Current 叠加其结果，磁盘与 mutate 永远只见真值。</param>
    public SettingsStore(AppSettings initial, Func<AppSettings, AppSettings>? bypass = null)
    {
        _disk = initial;
        _bypass = bypass;
        _effective = bypass is null ? initial : bypass(initial);
    }

    /// <summary>
    /// 从磁盘建 store。解析不了的文件先改名为
    /// <c>settings.json.bad-yyyyMMdd-HHmmss</c> 保留再返回默认值——覆盖
    /// 一个已知的坏文件没有任何收益，保留它至少留着手工挽回的余地。
    /// 读不出来（占用/无权限）时同样返回默认值但不改名：文件还在原地，
    /// 下次启动还有机会读对。
    /// </summary>
    public static SettingsLoadResult Load(string path, Func<AppSettings, AppSettings>? bypass = null)
    {
        if (!File.Exists(path))
        {
            return new SettingsLoadResult(new SettingsStore(new AppSettings(), bypass), null);
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return new SettingsLoadResult(new SettingsStore(new AppSettings(), bypass), null);
        }
        catch (UnauthorizedAccessException)
        {
            return new SettingsLoadResult(new SettingsStore(new AppSettings(), bypass), null);
        }

        if (AppSettings.TryParse(json, out var parsed))
        {
            return new SettingsLoadResult(new SettingsStore(parsed, bypass), null);
        }

        var quarantined = path + ".bad-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        try
        {
            File.Move(path, quarantined);
        }
        catch (IOException)
        {
            // 改名失败（重名/被占用）就不改了：内容反正已解析不了，下一次
            // 成功的 Update 会写新文件，旧内容留在原处或新名下都无所谓。
            return new SettingsLoadResult(new SettingsStore(new AppSettings(), bypass), null);
        }

        return new SettingsLoadResult(new SettingsStore(new AppSettings(), bypass), quarantined);
    }

    /// <summary>
    /// 在最新磁盘真值上应用 <paramref name="mutate"/>，写盘成功后更新
    /// Current 并广播。写盘失败（占用/无权限）抛
    /// <see cref="SettingsSaveException"/>，Current 原地不动——内存与磁盘
    /// 要么都改、要么都没改。
    /// </summary>
    /// <returns>广播时刻的最新生效值（含旁路叠加）。</returns>
    public AppSettings Update(Func<AppSettings, AppSettings> mutate, string path)
    {
        lock (_gate)
        {
            var updated = mutate(_disk);
            try
            {
                updated.Save(path);
            }
            catch (IOException failure)
            {
                throw new SettingsSaveException(path, failure);
            }
            catch (UnauthorizedAccessException failure)
            {
                throw new SettingsSaveException(path, failure);
            }

            _disk = updated;
            _effective = _bypass is null ? updated : _bypass(updated);
        }

        // 锁外广播，且广播与返回的都是此刻的最新生效值：处理器里若再
        // Update（窄条钉一类的级联改动），后触发的广播就不会比先触发的旧。
        var latest = Current;
        Changed?.Invoke(latest);
        return latest;
    }
}
