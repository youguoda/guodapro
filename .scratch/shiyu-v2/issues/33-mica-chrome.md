# 33 — 管理窗/设置窗的 Mica 材质（无边框 chrome 重写）

**What to build:** 把管理窗与设置窗从标准 chrome 改为无边框分层窗口，接入
DWM **Mica** 材质（BackdropKind.Mica 已在票 30 的 Backdrop 里实现，仅待窗口
形态满足票 03 的结论：材质只在分层窗口上渲染）。自绘标题栏负责拖拽、双击
最大化、最小化/关闭三钮。

## 为什么是独立一张票

票 30 实测确认：普通 WPF 窗口的重定向表面不透明，材质会被盖成黑色——
所以 Mica 的前提是窗口转分层（AllowsTransparency）+ 无边框。这不是一行
改动：两个窗口要各自补齐拖拽、圆角下内容裁剪、ResizeMode 边框热区
（WindowChrome）与关闭/最小化按钮。数值不复杂但面广，故单独立票。

**Blocked by:** None。

**Status:** done (succession via v3 - see docs/manual-test-v5.md)

- [x] 管理窗、设置窗转无边框分层窗口，自绘标题栏（拖拽/双击最大化/
      最小化/关闭，命中区 34-40 宽）
- [x] Backdrop.Attach(kind: Mica) 生效，浅色/深色跟随 ThemeManager
      （DWM 20 同步，已有机制）
- [x] 窗口圆角 12 + 内容圆角裁剪（最大化时去圆角、去阴影边距）
- [x] 跨屏拖动时 DPI 重缩放正确（PMv2；S1 最大化精确覆盖工作区
      0,0,2560,1392 实测）
- [x] 缩放窗口/最大化状态下材质与圆角表现正确（UpdateMaximizeVisuals）
- [x] 466 测试全绿；截图探针逐窗验证（20-maximized、30/31-library）

## 实现记录（2026-09-27）

- WindowChrome（CaptionHeight=0 + ResizeBorderThickness=6）保留系统缩放
  边框热区，标题栏自绘；TitlebarChrome 静态类统一拖拽/双击最大化/按钮
  排斥（DragMove 嵌套陷阱防御）。
- **修复无边框窗口最大化每边溢出 ~7px**：UpdateMaximizeVisuals 在最大化
  时手动 MoveWindow 到工作区（State Changed 钩子触发）；S1 探针实测
  修复前 -7,-7,2567,1447 → 修复后 0,0,2560,1392 精确贴合。
- 验证：466 单元 + S1 六断言全过（最大化/还原/拖拽/关闭）；
  S2 双栏三断言全过（详情填充/窄屏折叠截图对比）。

**Acceptance succession (2026-10-03, ticket 28):** closed via v3 - machine-testable parts are covered by the probe suite (28 checks, 0 fail) and 938+3 automated tests; human-judgement items live in docs/manual-test-v5.md.
