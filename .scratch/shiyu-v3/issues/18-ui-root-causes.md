# 18 — UI 根因：隐式文字样式、窗口根字号、文字透明度

**来源：** UI 报告 §2（R1、R4、R6）、U-01；优化报告 O-28
**Blocked by:** —
**Status:** ready-for-agent

**What to build:**
- 删除 App 级隐式 `TextBlock` 样式里的 `Foreground` 和 `FontFamily`（`Themes/Controls.xaml:16-19`），改在每个窗口根元素上设置 `TextElement.FontFamily / FontSize / Foreground`。这一条同时修好标题栏字形变豆腐块、删除钮危险色失效、分段选中文字只有 3.0:1、面板禁用按钮"灰盒子配黑字"。
- 设置窗根节点设字号，消灭 12 的回落（R4）。
- 文字上的 `Opacity` 字面量全部去掉，改用颜色令牌（R6，说明文字现在只有约 3.5:1）。

**验收：**
- [ ] 管理窗、设置窗标题栏三个按钮显示为正确字形
- [ ] 分段选中文字对比度 ≥ 4.5:1
- [ ] 删除钮 hover 时呈 Danger 色（取样 #C0392B ±4）
- [ ] 全仓 `Themes/` 以外，文字上没有 `Opacity` 字面量
