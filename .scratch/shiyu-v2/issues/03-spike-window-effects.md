# 03 — 技术验证：背景模糊与非激活窗口

**这是一张验证票，不是功能票。**

**What to build:** 一个圆角、半透明（约 95%）、带背景模糊的浮动面板窗口，且**不接受激活**
——点它不会把焦点从用户正在输入的地方抢走。

> **范围降级（2026-09-26）**：原票要求 Win10 + Win11 双系统实测；开发机只有 Win11，
> 经用户拍板**只按 Win11 验收**。Win10 的机制（未公开合成属性）记录为"未验证"，
> 发布前如需支持再补，不阻塞后续票。

## 为什么必须先验证

背景模糊在 Windows 上没有公开稳定的 API。Win11 走 `DwmSetWindowAttribute` 的
系统背景类型，Win10 只能走一个**未公开**的窗口合成属性——后者随系统更新行为变过，
且在某些版本上会带来明显的拖影。

拾语已有的徽标与面板窗口已经做到了"不接受激活"，但**没有做过圆角 + 半透明 + 模糊
三者叠加**。WPF 的 `AllowsTransparency` 与 DWM 的模糊在历史上是互斥的——开了前者，
后者往往失效。这一条不试出来，视觉稿就是空中楼阁。

同样要给退化路径：若模糊在目标系统上不可靠，纯半透明 + 阴影能做到什么程度。

**Blocked by:** None — can start immediately.

**Status:** done (succession via v3 - see docs/manual-test-v5.md)

## 结论（2026-09-26，Win11 本机实测）

样例：`spikes/WindowEffectsSpike/`（六种配方，stdout 打印 DWM 调用结果）；探针
`%TEMP%\fxb.ps1` 在窗口后垫洋红/白条纹底图逐像素采样并存屏（`shiyu-fx-r*.png`）。
条纹是判据：清晰=纯 alpha 透出、晕开=真模糊、均匀灰=材质生效。两处探针坑已除：
无 dispatcher 泵的 WPF 底图窗口画出来是纯白（配方 5/6 改由样例进程自带条纹底图）；
`GetWindowTextW` 经 Add-Type 只回 1 个字符（改从 stdout 解析 hwnd）。

1. **圆角**：`DWMWA_WINDOW_CORNER_PREFERENCE(33)=DWMWCP_ROUND(2)`，任意窗口形态
   均返回 S_OK 且肉眼可见（配方 2/4/5/6 截图确认圆弧干净、无锯齿黑边）。
   分层窗口（`AllowsTransparency`）用 WPF 自绘 `CornerRadius` 也能圆，两条路都通。
2. **半透明**：`AllowsTransparency` + 半透明画刷逐像素精确合成（配方 1 实测
   43,43,43，与 #F2202020 叠白的公式值完全一致）。
3. **背景材质（Mica/Acrylic，`DWMWA_SYSTEMBACKDROP_TYPE(38)`，Mica=2/Acrylic=3）——
   关键发现：材质只在分层窗口上可见。**
   - 普通窗口（无 `AllowsTransparency`）：画刷 alpha 再小也渲染为**纯黑**
     （配方 2/3 center=0,0,0）。机制：DWM 把材质画在窗口后面，但普通 WPF 窗口的
     重定向表面不透明（黑），把材质整个盖住。调用返回 S_OK——**成功不等于可见**。
   - 分层窗口 + 属性 38：材质**真的渲染**（配方 5 Acrylic/6 Mica center=176/202
     均匀灰，背后条纹完全消失=被材质吃掉；圆角缺口处能看到洋红=底图确实在场）。
     文字清晰、无伪影。即"WPF 分层与 DWM 模糊互斥"的旧说法在 Win11 公开 API
     路径上不成立。
   - 观感：系统暗色 tint 很重，材质读作"均匀灰/深石板"，不是高透明亚克力。
     想要"看穿"用 WPF 自身 alpha（配方 1）；想要"原生材质"用属性 38（配方 5/6）；
     两者可叠加（半透明画刷叠在材质上）。
4. **非激活**：本票探针的点击焦点检查不可信（探针自身前台锁 + 样例窗未加
   `WS_EX_NOACTIVATE`，普通窗被点激活是预期行为），不作为证据。非激活沿用票 01
   OverlaySpike 的结构验证：`ShowActivated=false` + `WS_EX_NOACTIVATE`
   （产品 `TransientWindow.MakeNonActivating`，徽标窗在用，实测不抢焦点）。
5. **多显示器/缩放**：本机 1.5x 缩放实测无锯齿、无黑边；多屏落点几何由 Core 的
   `BadgePlacement` 测试覆盖，DPI 换算走 `PresentationSource` 现有机制。

**退化路径**：属性 38 在旧系统上调用失败就跳过——外观自动退化为配方 1（纯半透明
+ 阴影），无需分支代码。

**给票 17/18 的落地配方**：`AllowsTransparency` + `CornerRadius 12` + DWM 33=2 +
38=3（transient Acrylic），外叠 #F2202020 级半透明画刷保持品牌深色；DWM 调用全部
容错（失败即跳过）。

- [x] 面板圆角、半透明、背景模糊在 Windows 11 上效果正确
- [ ] ~~在 Windows 10 上的实际效果被记录下来~~（降级为"未验证"，见上方说明）
- [x] 面板**不接受激活**：点击它之后，原应用的光标与选区不变（证据=票 01 结构验证
  + 产品徽标窗实测；本票探针点击检查不可信，已在结论 4 注明）
- [x] 窗口在多显示器与不同缩放下渲染正确，边缘无锯齿或黑边（1.5x 实测 + Core 测试）
- [x] 结论写进票内：各系统版本的可用性、所用机制、以及不可用时的退化外观

**Acceptance succession (2026-10-03, ticket 28):** closed via v3 - machine-testable parts are covered by the probe suite (28 checks, 0 fail) and 938+3 automated tests; human-judgement items live in docs/manual-test-v5.md.
