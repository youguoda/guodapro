# 16 — App 层逻辑下沉 Core

**来源：** 优化报告 O-27、O-43（热键部分）；审计 A2 §5
**Blocked by:** 07、10（均已完成）
**Branch:** `v3/core`（三提交 f4c2fab/69d40d7/92fc444 已并 master，快进）
**Status:** done

实施记录（2026-10-02）：`HotkeyPlan`（f4c2fab——四键解析、逐对点名冲突含快速条、缺修饰键拒绝；三调用方统一，引导误报缺陷修复有回归测试；冲突键不再注册属改善性微变）；`SelectionDebt`（69d40d7——Offer 顶替先还旧/PanelDisplayed 含失败路径/Exit 新债先还旧债压轴，恰好一次结算为受测不变式，App 的 `_pendingSelection` 字段删除、OnExit 首行结清含面板在途债）；`BarRefreshPolicy`（92fc444——Shown 必重读/Hidden(reason) 全套收尾/StoreChanged 可见且非自写恰好一次 Reload；计时器职责注明已由票 14 PreviewPolicy 承担）。新增 33 测试（HotkeyPlan 11+SelectionDebt 12 含 FakeCapturePlatform 端到端+BarRefreshPolicy 10），**868 全绿**。

**What to build**（照 Core 已有的"端口 + 假实现 + 纯状态机"套路，`PreviewPolicy` 是样板）：
- `HotkeyPlan.Build(AppSettings) → (bindings, problems)`：设置窗、引导、App 注册三处共用。顺带修掉：引导只校验 3 个热键，与快速条冲突时误报"被其他软件占用"。
- `SelectionDebt`：划词"借出—还原"的账本。新徽标顶替旧账、淡出、点击、面板失败、退出——每种路径都保证恰好还原一次。复用 `FakeCapturePlatform`。
- `BarRefreshPolicy`：窄条显隐与刷新的状态机（显示、隐藏原因、存储变更、计时），配合票 12 的 `EntryStore.Changed`。

**验收：**
- [ ] 三个模块各有完整的单测
- [ ] App 层对应代码只剩"把决策应用到窗口"的薄层
- [ ] 引导里设置与快速条相同的热键时，当场报"与快速条冲突"
