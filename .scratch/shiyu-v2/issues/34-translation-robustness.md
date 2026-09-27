# 34 — 翻译质量级：回声检测 + prompt 低温 + 错误码映射 + 本地语种先验

**What to build:** 落实 Glossy 调研（docs/research/2026-09-27-glossy-analysis.md
§9.4）的"翻译健壮性包"——纯 Core 改动，一天级，立刻提升每一次翻译的可靠性与观感：

- `TranslationEcho.IsEchoish(original, translated)` 纯函数（归一化后相似度阈值）：
  "回声"= 把原文当译文吐回来。命中且用户未强制源语言时自动换目标语言重试一次；
  重试仍回声则如实展示。
- `TranslationPrompt` 默认 `temperature=0.2`（Glossy 实证的翻译任务合理低温），
  覆盖口留出。
- 错误码映射表：429/529/503 等瞬态码以 [600,1400]ms 退避重试两次；401/402/余额类
  直接给人话报错（"凭据无效/余额不足"），不再抛裸异常。
- 本地语种先验 `LanguageGuess.FromText`（CJK/西里尔/拉丁码位统计），供回声检测
  与面板方向标签使用——不为拿 sourceLang 破坏流式。

**价值:** 每一次翻译的可靠性提升；不依赖任何新通道或服务端。

**Blocked by:** None。

**Status:** ready-for-agent

- [ ] TranslationEcho 纯函数 + 单测（回声/非回声/标点差异/大小写边界）
- [ ] 回声触发的一次换向重试（仅当用户未强制源语言）
- [ ] temperature=0.2 进默认翻译请求
- [ ] 瞬态码退避重试两次 + 终态码人话映射（Core 单测覆盖映射表）
- [ ] LanguageGuess 码位统计纯函数 + 单测（中/日/韩/西里尔/拉丁/混合）
- [ ] 面板方向标签使用本地先验（流式不受影响）
- [ ] 全量测试绿；真后端翻译探针回归
