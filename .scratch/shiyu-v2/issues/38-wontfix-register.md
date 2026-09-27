# 38 — wontfix 存根：明确不做项 + 发布守则落 docs

**What to record:** Glossy 调研（docs/research/2026-09-27-glossy-analysis.md
§9.5）的"明确不做"与一条发布守则，立此存照——未来重议时引用本票，不丢上下文。

## 明确不做（含理由）

- **Google 非官方端点 + 伪装 UA**：灰色用法，封禁与合规风险；Glossy 自己都要
  靠双 client + 120s 冷却硬扛——不做。
- **单位货币换算**：与拾语"效率工具"定位弱相关（Glossy 是阅读工具所以合理）
  ——不做；用户反馈需要时重议。
- **剪贴板轮询取词**：Glossy 非 Windows 才轮询；拾语已事件驱动——不做。
- **UIA 上下文句**（单词卡"所在原句"）：有价值但 UIA TextPattern 的应用
  兼容性是泥潭——backlog，待票 35 单词卡跑通后再评估。

## 采纳的发布守则（本票落地）

Glossy 最大红旗：v1.5.x release notes 描述了 OCR/文档翻译/回填，但对应 tag
源码里不存在这些功能（公开仓库滞后于私有开发）。教训直接适用于拾语票 28 的
GitHub Releases 更新通道。

**守则：release notes 描述的每个功能，必须能在对应 tag 的源码里指出来源。**

- [x] docs/release-checklist.md 落此守则并入发布流程
- [x] 本票作为"明确不做"的存根
