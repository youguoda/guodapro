# 19 — 设计令牌落地与共享组件

**来源：** UI 报告 §4（4.1–4.9）；优化报告 O-29；ADR-0012
**Blocked by:** 18（已完成）
**Status:** done（令牌层+组件层+双 spike 已并；86 处迁移+静态检查已并）

**Spike 2 结论（2026-10-02，主控亲做，分支 `v3/spike-fluent` 未并）：选路线 B（Controls.xaml 手写模板）。**
试验：`ThemeManager` 在 MergedDictionaries 最前并入 `pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml`（令牌与 Controls.xaml 后加、优先），构建通过、探针实例设置窗渲染无恙。**像素证据：保存按钮的边缘轮廓与 Aero2 基线逐行一致（完全零变化）**——我们的隐式控件样式在查找序上压过 Fluent 的隐式样式，而我们的样式无 `BasedOn`，模板回落仍是 Aero2 主题默认；要让路线 A 生效须给每个控件样式挂 Fluent 键的 BasedOn 继承，工作量与手写模板相当且押在实验性 API（WPF0001）上。**决定：路线 B**——为用到的约 10 类控件手写模板（可拿 Fluent.xaml 源码当参考实现抄形状与视觉状态），令牌单一来源不受影响。

**Spike 1 结论（2026-10-02，主控亲做，分支 `v3/spike-mica` 未并）：非分层 Mica 配方成立。**
配方：`WindowStyle="SingleBorderWindow"` + `AllowsTransparency="False"` + `WindowChrome.GlassFrameThickness="-1"` + `Background="Transparent"` + `OnSourceInitialized` 里 `CompositionTarget.BackgroundColor = Colors.Transparent`；壳层去掉自绘边框/圆角/`DropShadowEffect`（阴影与圆角交还系统，DWMWA 38=2 照旧）。**证据**（探针 PrintWindow 像素采样，对照旧分层版）：新窗口四角/边缘为一致的壁纸色（R239 G249 B221——桌面绿透过 Mica），旧版为纯中性灰（243/243/243，`SurfaceMaterial` 近不透明盖死的实证）；内容完整渲染、重定向面不黑、崩溃无。**推翻票 03 的"材质只在分层窗口可见"路径依赖，R7 的三宗罪（材质被盖/失 ClearType 前置/无边框无贴靠）一并解除**（ClearType 数值上未证——色度采样被彩色 UI 干扰，由实施票 23 时用 visual-judge 或肉眼复核；Effect 子树已移除是其前置条件成立）。票 23（设置窗）与票 24（管理窗）照此配方实施；spike 分支的 XAML 改动可直接作起点。

**组件层已并 master（2026-10-02 21:20 收口）**：代理完成组件库（3c28f50：隐式 Button/TextBox/PasswordBox/CheckBox/ComboBox/ListBoxItem/TabItem 全六态 + 键控 FlyoutButton/AccentButton/DangerButton/IconButton/KeyCap/SearchBox/ToggleSwitch/NumberBox/InfoBar/EmptyState/Divider/ContentDialog + 配套类，KindTab 共享，Radius.Pill=16）；主控接管窄条迁移（BarWindow.xaml 净瘦 95 行、空状态组件化）。**探针两处基建缺陷修复后 run-all 18/20 全绿**：①Get-WindowShot 出口重绘（FromHbitmap 与 GDI 共享内存致 LockBits 读陈旧缓冲——quickbar 选中与删除钮 Danger 曾被误读为红）；②键帽判定宽度上限 38 DIP。**票 19 自有红项全绿**（键帽 28×18 DIP 墨迹 7.3 DIP 高、删除钮 rgb=192,57,43、快速条选中 107 accent 像素）；余 2 红属票 20（面板双圆角）与票 22（英文异常）。**票 19 剩余两件事（未做）**：86 处旧字号档（Size.*/Font*）迁移到 Type.* 并退役（编译器驱动，控件 vs 内容需逐处判断）；静态检查脚本（Themes/ 外禁字面量字号/圆角/文字透明度）进 CI。

**令牌层已由主控亲做并并 master（2026-10-02，63d348b）**：新色槽 12 个两套调色板齐备（CardStroke/StrokeStrong/Divider/AccentSubtle/Selected/DangerSubtle/TextOnDanger/FocusOuter/FocusInner/Success/Caution/Brand，成对纪律与 Slots 完备性自动覆盖）；SurfaceMaterial α 降至 D9/E0 并有测试钉住不许写回；字阶 v2 全档（Type.* 常量 + ThemeManager 导出，旧 Font* 档并存待模板迁移后退役）；图标档 12/16/20/24/48；语义间距 Space.1–12 与控件尺寸档（Control.Height 等）已导出；非文本 3:1（StrokeStrong/FocusOuter）与叠层透明契约（OverlaySlots alpha<FF）入 DesignTokenTests。**代理剩余范围**：共享组件（IconButton/FlyoutButton/AcccentButton/两种 KeyCap/SearchBox/ToggleSwitch/NumberBox/ComboBox/ListRow/InfoBar/ContentDialog/EmptyState，先抽 BarWindow 里锁着的样式）、全仓迁移到 v2 令牌与退役旧档、静态检查（Themes/ 外禁字面量字号/圆角/文字透明度）、Aero2 清零的像素验收。789 全绿基线。

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
