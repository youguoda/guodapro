# 19 — 设计令牌落地与共享组件

**来源：** UI 报告 §4（4.1–4.9）；优化报告 O-29；ADR-0012
**Blocked by:** 18
**Status:** ready-for-agent

**What to build:**
- **两个 spike 先行：**
  - .NET 9 第一方 Fluent 主题字典映射到现有令牌（路线 A），对比手写模板（路线 B），选定一条；
  - 常规窗的非分层 Mica 配方（不行就用官方回退色）。结论写进本票。
- **令牌：**
  - 字号：`Type.*`，控件 14、内容 18 可调；
  - 间距：4 DIP 网格 `Space.*`，导出为资源；
  - 圆角：`Control` 4、`Overlay` 8，胶囊 = 高 / 2；
  - 色槽：新增 `CardStroke`、`Stroke.Strong`、`Divider`、`AccentSubtle`、`Selected`、`Focus.*`、`TextOnDanger`、`Success`、`Caution`、`Brand`；`SurfaceMaterial` 的 α 改为 D9 / E0。
- **测试：**
  - `ReadablePairs` 补上实际画出来的组合（例如 TextTertiary 配 SurfaceSubtle 只有 4.33:1，先让它变红）；
  - 静态检查：`Themes/` 以外出现字面量的字号、圆角、文字 `Opacity` 即报红。
- **共享组件**（放 `Themes/`，浮层与常规窗共用）：`IconButton`、`FlyoutButton`、`AccentButton`、两种 `KeyCap`、`SearchBox`、`ToggleSwitch`、`NumberBox`（带单位）、`ComboBox`、`ListRow`、`InfoBar`、`ContentDialog`、`EmptyState`。先把锁在 `BarWindow.xaml` 里的样式提出来。
- 15 处 emoji 和符号字符全部换成 Segoe Fluent 字形（按票 39 的双字体 cmap 探测法核对）。

**验收：**
- [ ] 每个组件都有完整的状态（静止、悬停、按下、选中、焦点、禁用），对照 UI 报告 §4.8 的状态矩阵
- [ ] 静态检查与 `DesignTokenTests` 为绿
- [ ] 深色主题下所有组件截图一轮，没有 Aero2 颜色
