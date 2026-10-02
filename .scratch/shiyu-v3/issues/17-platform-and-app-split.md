# 17 — 平台层收口、App 拆分、生产构建不读调试变量

**来源：** 优化报告 O-39、O-40、O-41、O-43（其余部分）
**Blocked by:** 16
**Status:** ready-for-agent

**What to build:**
- App 层的 7 个 `DllImport` 移到 `Shiyu.Windows`，合并 6 组重复声明。
- `App.xaml.cs`（830 行）拆成生命周期宿主，加按功能划分的模块（翻译、取词、窄条、管理窗、备份、更新），每个模块订阅 `SettingsStore.Changed`。
- 生产构建不读取 `SHIYU_*` 环境变量（只在调试构建生效）。
- 小修：
  - 面板持有已释放的热键注册表，并把它重新挂回消息窗口——改为持有 `Func<HotkeyRegistry>`，释放后调用直接抛异常；
  - `Process.Start` 的返回值都要 Dispose。

**验收：**
- [ ] 在 `src/Shiyu.App` 下 grep `DllImport` 结果为 0
- [ ] `App.xaml.cs` 少于 250 行
- [ ] 在 Release 构建中设置 `SHIYU_*` 变量，行为不变
