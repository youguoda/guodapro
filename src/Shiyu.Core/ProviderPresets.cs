using System.Text.Json.Nodes;

namespace Shiyu.Core;

/// <summary>
/// 一家 OpenAI 兼容服务商的"开箱即用"参数。全部数据出自 2026-10-01 的
/// 官方文档调研（依据存档于 provider-presets.md），改任何数值都要重新核实，
/// 不要凭记忆填。
/// </summary>
/// <param name="Id">稳定标识，写进 <see cref="AppSettings.BackendPresetId"/> 持久化；换名字等于丢掉存量用户的预设。</param>
/// <param name="BaseUrl">拼 <c>/chat/completions</c> 前的根地址，不带尾斜杠。</param>
/// <param name="DefaultModel">选中预设即填入的模型。</param>
/// <param name="AltModels">同服务商的合理备选；仍算"没手改过"，预设继续生效。</param>
/// <param name="ApiKeyUrl">服务商申请密钥的页面，「申请密钥」链接指向它。</param>
/// <param name="ExtraBody">附加到请求体顶层的字段（各家关思考的写法不同）；null 表示标准请求体即可。</param>
/// <param name="MaxTemperature">温度上限（Min(请求值, 该值)）；null 表示不设限。</param>
/// <param name="SendTemperature">false 表示整个 temperature 字段都不发——Kimi 的温度是固定值，传别的直接报错。</param>
public sealed record ProviderPreset(
    string Id,
    string DisplayName,
    string BaseUrl,
    string DefaultModel,
    string[] AltModels,
    string ApiKeyUrl,
    JsonObject? ExtraBody,
    double? MaxTemperature = null,
    bool SendTemperature = true);

/// <summary>
/// 服务商预设清单（票 08 / ADR-0009）。前四家进默认下拉——大陆直连、
/// OpenAI 兼容、各自能关思考或不思考；Kimi 兼容性差（温度为固定值），
/// 放「更多」；火山方舟的 model 字段格式未核实，不进预设。
/// </summary>
public static class ProviderPresets
{
    /// <summary>默认下拉的四家，顺序即显示顺序：百炼唯一"不加字段也不思考"，排第一。</summary>
    public static readonly IReadOnlyList<ProviderPreset> Primary =
    [
        new ProviderPreset(
            "bailian",
            "阿里云百炼（通义千问）",
            "https://dashscope.aliyuncs.com/compatible-mode/v1",
            "qwen-flash",
            ["qwen-turbo"],
            "https://bailian.console.aliyun.com/cn-beijing/model/settings/api-key",
            ExtraBody: null,

            // 官方取值范围 [0, 2)：2 本身不合法，上限收在 1.99。
            MaxTemperature: 1.99),
        new ProviderPreset(
            "deepseek",
            "DeepSeek",
            "https://api.deepseek.com",
            "deepseek-flash",
            ["deepseek-v4-pro"],
            "https://platform.deepseek.com/api_keys",
            ExtraBody: new JsonObject { ["thinking"] = new JsonObject { ["type"] = "disabled" } },
            MaxTemperature: 2.0),
        new ProviderPreset(
            "zhipu",
            "智谱 GLM（免费模型）",
            "https://open.bigmodel.cn/api/paas/v4",
            "glm-4-flash-250414",
            ["glm-4.7-flash"],
            "https://bigmodel.cn/usercenter/proj-mgmt/apikeys",
            ExtraBody: null,

            // GLM-4 代的 temperature 上限是 1.0，两位小数。
            MaxTemperature: 1.0),
        new ProviderPreset(
            "siliconflow",
            "硅基流动 SiliconFlow",
            "https://api.siliconflow.cn/v1",
            "deepseek-ai/DeepSeek-V4-Flash",
            [],
            "https://cloud.siliconflow.cn/account/ak",
            ExtraBody: new JsonObject { ["enable_thinking"] = false },
            MaxTemperature: 2.0),
    ];

    /// <summary>「更多」里的预设：能用，但有需要用户自己知道的脾气。</summary>
    public static readonly IReadOnlyList<ProviderPreset> More =
    [
        new ProviderPreset(
            "kimi",
            "Kimi（月之暗面）",
            "https://api.moonshot.cn/v1",
            "kimi-k2.6",
            [],
            "https://platform.kimi.com/console/api-keys",
            ExtraBody: new JsonObject { ["thinking"] = new JsonObject { ["type"] = "disabled" } },
            MaxTemperature: null,

            // Kimi 的 temperature 是固定值（非思考 0.6），传别的值报错——干脆不发。
            SendTemperature: false),
    ];

    public static IReadOnlyList<ProviderPreset> All => [.. Primary, .. More];

    public static ProviderPreset? Find(string? id)
        => All.FirstOrDefault(preset =>
            string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 按设置的现状解析生效的预设：地址与模型仍与预设一致（模型取默认或
    /// 备选都算）才算选着了预设；用户手改过地址或模型就视为自定义，
    /// 预设的附加字段与温度规则随之失效——手填的模型多半喂不起别家的
    /// 扩展字段。这是数据层的最终裁决，界面上的清空只是让用户看得见。
    /// </summary>
    public static ProviderPreset? ResolveFor(AppSettings settings)
    {
        var preset = Find(settings.BackendPresetId);
        if (preset is null)
        {
            return null;
        }

        var model = settings.BackendModel.Trim();
        return settings.BackendBaseUrl.TrimEnd('/') == preset.BaseUrl.TrimEnd('/')
            && (model == preset.DefaultModel
                || preset.AltModels.Contains(model, StringComparer.Ordinal))
            ? preset
            : null;
    }
}

/// <summary>
/// 公共通道（公共中转）当前的上线状态（ADR-0009）。它不是一段代码写完
/// 就算数的开关，而是一项运营承诺的三个条件，全部满足才翻成 true：
///
/// <list type="number">
/// <item>绑定自定义域名并在大陆网络实测可达——默认的 *.workers.dev
/// 域名在大陆整域被 DNS 污染，部署了也连不上；</item>
/// <item>服务端加固完成（v3 票 05：语言字段注入、全局额度可被耗尽、
/// IPv6 未聚合、额度计数非原子、盐缺失回退）；</item>
/// <item>有人愿意承担模型调用费用和日常运营。</item>
/// </list>
///
/// 三个条件此刻都不满足，因此为 false：引导与设置里公共通道显示
/// 「即将推出」且不可选；已选它的存量用户按票 08 的迁移策略处理。
/// 服务端代码（server/）保留并继续加固，等条件齐了翻这一个常量即可。
/// </summary>
public static class RelayChannel
{
    public const bool Available = false;
}
