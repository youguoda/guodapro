# 27 — 可访问性基线

**来源：** UI 报告 U-29、§4.8；优化报告 O-35
**Blocked by:** 23、24
**Status:** ready-for-agent

**What to build:**
- 所有图标按钮设置 `AutomationProperties.Name`（现在全仓为 0 处）；状态变化（已复制、错误）用 `LiveSetting` 朗读。
- 焦点视觉：双层焦点环（`Focus.Outer` 2 DIP + `Focus.Inner` 1 DIP，圆角 = 控件圆角 + 2）。现在全仓 `FocusVisualStyle` 为 0 处。
- 对比度测试覆盖实际画出来的组合；非文本对比（输入框边界、开关描边）≥ 3:1。
- 跟随系统"辅助功能 › 文本大小"（`UISettings.TextScaleFactor`），作用于内容字号与控件字号。
- 尊重"减少动画"的现有机制保持不变。

**验收：**
- [ ] 讲述人能读出每个图标按钮的名字
- [ ] 每个窗口里按 Tab 遍历，焦点处处可见
- [ ] 系统文本大小调到 150% 时布局不重叠、不截断
