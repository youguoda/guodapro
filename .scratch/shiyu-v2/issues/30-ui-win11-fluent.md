# 30 — Windows 11 融合 UI 改造（Fluent 材质与现代化控件）

**What to build:** 把拾语全部界面向 Windows 11 原生观感收敛：Fluent 材质分层
（长寿命窗用 Mica、瞬态浮层用 Acrylic）、4/8px 圆角体系、Segoe UI Variable 字阶、
静谧动效。参照口碑最好的同类工具与微软官方设计规范，全部落在现有
DesignTokens/ThemeManager 架构上——**不引第三方 UI 库**。

用户原话锚定：「需要的都是比较现代化的，有些控件可以参考 Windows 11 的设计」；
硬约束：「项目的功能都是完好的」——每一步改造挂现有测试与实机探针回归。

## 设计目标："现代化 + 融入 Win11" 由五件事构成

1. **材质分层**——原生感的最大来源。Mica（不透明、采样壁纸）给长寿命窗；
   Acrylic（实时模糊）给瞬态浮层。拾语六个窗口恰好二分。
2. **几何**——圆角双档（控件 4px / 浮层 8px，窗口级 12px）、4px 间距网格。
3. **字阶**——Segoe UI Variable 优先（中文回退 YaHei UI 已有），字号向官方字阶
   对齐（Caption 12 / Body 14）。
4. **静谧动效**——现有 MotionPlan（160/300ms、遵守系统"减少动画"）已对齐，
   本票只做曲线与对象收敛，不新做动效系统。
5. **图标**——系统级符号字体（Segoe Fluent Icons），见下节；杜绝自绘位图
   与第三方图标风格混入。

## 标杆整理（网络调研，好评共识）

| 标杆 | 口碑定位 | 学什么 | 不学什么 |
|---|---|---|---|
| Win11 原生 Win+V | 用户心中的"原生基线" | 卡片密度、置顶区、大预览、键盘优先 | GIF 面板等拾语没有的功能 |
| PowerToys Advanced Paste | WinUI 浮层范本（与翻译面板同形态） | 浮层材质、动作排布、圆角与阴影分寸 | 其依赖 WinUI 的实现路径 |
| EcoPaste | 轻量现代中文友好（v2 既定参照） | 托盘优先交互、卡片留白 | Tauri 技术栈 |
| PasteBar | 精致现代、用户赞"常驻配置" | 列表行节奏、暗色一致性 | 板块/片段等超集功能 |
| Ditto / CopyQ | 强大但 UI 老（反面参照） | 功能组织 | 观感——恰是本票要超越的 |

## 官方规范要点（实施时对照）

- 材质分工：[Materials used in Windows apps](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/materials)、[Mica](https://learn.microsoft.com/en-us/windows/apps/design/style/mica)、[Acrylic](https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic)
- 几何：[Windows 11 Geometry](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/geometry)——ControlCornerRadius 4px / OverlayCornerRadius 8px
- 字阶与令牌：[Fluent 2](https://fluent2.microsoft.design/get-started/design)
- WPF 落地：`DwmSetWindowAttribute` + `DWMWA_SYSTEMBACKDROP_TYPE(38)`（2=Mica/3=Acrylic，
  需 Win11 22H2+）+ `DWMWA_USE_IMMERSIVE_DARK_MODE(20)` 同步暗色；
  参考 [tvc-16 实测指南](https://tvc-16.science/mica-wpf.html)、[dotnet/wpf#8545](https://github.com/dotnet/wpf/issues/8545)
  （WPF 一等支持仍在追踪，本票走 DWM 直调）。

## 图标体系：系统级符号字体（SF Symbols 的 Windows 对等物）

需求原话：图标要求"系统级 SF Symbols"。**SF Symbols 是 Apple 生态的专属
符号系统**，Windows 上不存在亦不可授权使用；它的系统级对等物是
**Segoe Fluent Icons**（Windows 11 自带的官方符号字体，在 Windows 里的地位
与 SF Symbols 在 macOS 上完全对应）。本票据此落规则：

- 全部图标用系统字形：`FontFamily="Segoe Fluent Icons"` + 官方字形码位；
  Win10 经 FontFamily 回退链落到 `Segoe MDL2 Assets`（同码位、字形略旧），
  不写系统版本分支。
- 实施时对照微软官方 Segoe Fluent Icons 字形表逐一核对；个别 Segoe 缺失的
  符号用微软开源 **Fluent UI System Icons**（同一设计语言）补位——仍然不引
  第三方风格图标集。
- 已可钉死的核心字形（实施时全表核对）：复制 E8C8、粘贴 E77F、删除 E74D、
  置顶 E718 / 取消 E77A、收藏 ☆E734 / ★E735、搜索 E721、设置 E713、
  标签 E8EC、打开 E8E5、刷新 E895、完成 E73E、关闭 E711、警告 E7BA、信息 E946。
- **悬停托盘按钮从"单汉字"（复/贴/删…）升级为系统字形图标**；汉字键帽提示
  不丢——C/O/P/S/N/G/D 键徽标体系（票 14/15）与右键菜单快捷键列原样保留，
  字形只接管"看得见的那一层"。
- 图标颜色随前景语义走（Brush.Text / Brush.TextSecondary / Brush.Danger），
  不引入独立彩色图标。

## 技术路径：票 03 已实测定案，本票零技术不确定性

直接采用 03 的实测配方（见 03 结论节）：

- **材质只在分层窗口上可见**：`AllowsTransparency` 窗口 + 属性 38 真实渲染
  （普通 WPF 窗口画刷 alpha 再小也渲染纯黑盖住材质——成功不等于可见）。
- 落地配方：`AllowsTransparency` + 自绘 `CornerRadius` + DWM 33=2（圆角）+
  38=3（Acrylic）/38=2（Mica），外叠 #F2 级半透明品牌画刷；**DWM 调用全部容错，
  失败即跳过**——外观自动退化为纯半透明 + 阴影（03 配方 1），零分支代码。
- 暗色同步：属性 20 跟随 ThemeManager 当前主题写入。
- 1.5x 缩放实测无锯齿（03 已验）。

**不引 WPF-UI 等第三方库的理由**：自绘度与品牌一致性是拾语的设计资产，
ThemeManager 即时换肤已成型；引库会架空 DesignTokens 并带来风格漂移。

## 材质映射（本票核心）

| 窗口 | 寿命 | 材质 |
|---|---|---|
| LibraryWindow（管理窗） | 长寿命 | Mica |
| SettingsWindow（设置窗） | 长寿命 | Mica |
| BarWindow（窄条） | 瞬态 | Acrylic |
| PanelWindow（翻译面板） | 瞬态 | Acrylic |
| QuickBarWindow（快速条） | 瞬态 | Acrylic |
| BadgeWindow（徽标） | 瞬态 | Acrylic（保持极简，仅换底） |
| 卡片右键菜单 Popup | 瞬态 | Acrylic 表面 |

## DesignTokens v2 修订清单（可直接改代码的粒度）

- 新增色槽（浅/深成对，WCAG 对比度不降）：`SurfaceMaterial`（叠在材质上的品牌
  tint，#F2 级 alpha）、`LayerFlyout`（浮层层叠底）、`StateHover`（5% 叠加）、
  `StatePressed`（更深的 8~10% 叠加）、accent 的 hover/pressed 色阶。
- Radius 收敛三档对齐 Win11：Small=4（控件）、Card/Overlay=8、Window=12
  （与 03 配方一致）。
- 字号向官方字阶微调：Hint/Caption 12、Secondary 13、Body 14、BodyLarge 16
  （现 11–15 档整体偏小一档）。
- 阴影收敛：浮层用 Win11 式"低扩散、大模糊、低不透明度"单阴影，去掉多重叠加。
- 图标令牌：`FamilyIcon = "Segoe Fluent Icons"`、`FamilyIconFallback = "Segoe
  MDL2 Assets"`（FontFamily 回退链），图标字号槽 IconSmall 12 / Icon 16 /
  IconLarge 20。

## 逐界面改造要点

- **窄条**：卡片 hover 从"换底色"改为 5% 状态叠加；选中态改 accent 描边 +
  叠加；头部两行做减法合并；列表行高对齐 4px 网格。
- **设置窗**：分段控件已是 Win11 形态（票 22）保持；TabControl 改下划线指示式
  页头；Mica 底。
- **管理窗**：Mica 底 + 列表行 40px 节奏 + 工具栏去边框化（按钮 ghost 化）。
- **翻译面板/快速条/徽标**：Acrylic + CornerRadius 8~12 + 阴影收敛（03 配方直接套）。
- **右键菜单**：Popup 表面换 Acrylic 底 + 4px 行叠加态。
- **图标落点**：窄条头部（搜索 E721、清除 E711、★ 收藏筛选 E734/735）、悬停
  托盘十动作字形化、管理窗工具栏（ghost 按钮 + 字形）、设置窗页头、右键菜单；
  来源应用的程序图标是位图缓存（票 09），不属于符号字体体系，保持不动。

## 实施顺序（小步，每步功能完好）

1. **tokens v2**：纯数据改动，ThemeManager 即时生效——397 测试 + 双主题视觉探针。
2. **BackdropHost**：DWM 33/38/20 封装（容错退化），先接管理窗与设置窗（Mica 最稳）。
3. **逐窗口 Acrylic**：窄条 → 面板/快速条/徽标 → 右键菜单，每窗口一个验收点，
   实机探针回归（窄条 UIA+真鼠标、翻译面板、轻量模式内存）。
4. **收尾**：浅/深/跟随系统三主题下材质渲染与暗色 DWM 同步验证；Win10 未验证项
   沿 03 约定保持记录。

**Blocked by:** None — 票 03 的 Win11 实测结论即本票地基（03 的 Win10 部分保持
"未验证"，不阻塞本票，发布前如需支持再补）。

**Status:** ready-for-human

- [x] DesignTokens v2 落地：材质层/状态色槽、4/8/12 圆角、字阶对齐；浅深双主题
      成对、WCAG 对比度不降（AccentHover 初版 4.49 被测试拦下，已调深）
- [x] Backdrop 封装 DWM 33/38/20，调用失败自动退化为半透明+阴影（03 配方 1），
      产品代码零系统版本分支
- [x] 窄条/翻译面板/快速条 Acrylic 真实渲染（探针截图壁纸透出模糊）；管理窗/
      设置窗本轮取得圆角+暗色标题栏，**Mica 转换推迟为后续票**——票 03 证明
      材质仅在分层窗口可见，两窗需无边框 chrome 重写；右键菜单维持不透明浮层
      （可读性优先），随该后续票一并处理
- [x] 浅色/深色/跟随系统三主题下，材质与 DWM 暗色标记同步正确（dark flag 随
      ThemeManager 每次换肤写入；暗色观感截图留人工，用户当前固定浅色）
- [x] 每界面改造后现有实机探针全绿（窄条 UIA+真鼠标：删除/撤销 Invoke、暗色
      切换流程；翻译面板、设置页签切换）
- [x] 全程测试套件绿（466）；不引第三方 UI 库；键盘模型与快捷键行为不变
- [x] 图标全部走 Segoe Fluent Icons（FontFamily 回退链落 MDL2），零自绘位图
      图标、零第三方风格混入；汉字键帽提示体系（C/O/P/S/N/G/D）不回退
- [x] Win10 未验证项保持记录（沿 03 约定）

## 实现记录（2026-09-26）

- 令牌 v2：新增 SurfaceMaterial(94% 半透明品牌 tint)/LayerFlyout/StateHover(5%)/
  StatePressed(9%)/AccentHover/AccentPressed 六槽（浅深成对，对比度测试扩到
  材质与 accent 变体）；Radius 收敛 4/8/12（Small/Card+Overlay/Window）；
  字号 Hint 11→12、BodyLarge 15→16 对齐 Fluent 字阶；FamilyIcon +
  IconSmall/Medium/Large；阴影改 Win11 式宽软低。
- Backdrop（App）：DWM 33=2 圆角、38=2/3 材质、20 暗色，全部 best-effort；
  ThemeManager 每次换肤调 SyncToTheme。窄条根 Border 改浮层壳（SurfaceMaterial
  + Radius.Window + 单阴影），header/footer 透明让材质整片透出。
- 探针发现的**三重真 bug**（均修）：① 窄条底部伸出工作区 280px——首次召唤
  PresentationSource 为 null → scale 静默 1.0 → Place 收 DIU 尺寸不翻转；
  改用 GetDpiForMonitor（Shcore）按光标所在屏取真实 scale。② 托盘字形 tofu——
  Content 字符串被隐式 TextBlock 样式强制 Font.Ui；改显式 TextBlock。
  ③ AccentHover 白字对比 4.49 不达 AA——测试拦下，调深。
- 截图证据：`%TEMP%\ui-shots\`（1-bar-rest、2-bar-tray 八字形+材质透壁纸、
  5-bar-dark、7-settings-data、10-settings-look、8-library、9-panel）。

