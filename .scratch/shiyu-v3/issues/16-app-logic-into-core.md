# 16 — App 层逻辑下沉 Core

**来源：** 优化报告 O-27、O-43（热键部分）；审计 A2 §5
**Blocked by:** 07、10
**Status:** ready-for-agent

**What to build**（照 Core 已有的"端口 + 假实现 + 纯状态机"套路，`PreviewPolicy` 是样板）：
- `HotkeyPlan.Build(AppSettings) → (bindings, problems)`：设置窗、引导、App 注册三处共用。顺带修掉：引导只校验 3 个热键，与快速条冲突时误报"被其他软件占用"。
- `SelectionDebt`：划词"借出—还原"的账本。新徽标顶替旧账、淡出、点击、面板失败、退出——每种路径都保证恰好还原一次。复用 `FakeCapturePlatform`。
- `BarRefreshPolicy`：窄条显隐与刷新的状态机（显示、隐藏原因、存储变更、计时），配合票 12 的 `EntryStore.Changed`。

**验收：**
- [ ] 三个模块各有完整的单测
- [ ] App 层对应代码只剩"把决策应用到窗口"的薄层
- [ ] 引导里设置与快速条相同的热键时，当场报"与快速条冲突"
