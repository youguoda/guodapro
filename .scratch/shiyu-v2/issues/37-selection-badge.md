# 37 — 交互级：划词徽章模式（WH_MOUSE_LL 拖选检测）

**What to build:** 落实 Glossy 调研（docs/research/2026-09-27-glossy-analysis.md
§9.3）——拖选即触发：选中文字后光标旁浮"译"徽标，点击才翻译。零记忆成本，
贴近阅读场景；复用现有徽标全套基建（BadgeWindow + Placement + 非激活）。

- **WH_MOUSE_LL 钩子**：回调只上报原始事件（照 WinVHook 纪律，钩子线程保持
  轻量），拖选手势分类（≥5px 位移）在 UI 线程判定；**拖选永不延迟**。
- **取词**：模拟 Ctrl+C（SelectionCapture 已有借出/还原）——采纳 Glossy 次序
  细节：**剪贴板还原放在翻译面板显示之后**（还原可以等，被复制的应用可能
  还要用）。
- **过滤链**：总开关 → 最小长度 → 须含字母 → 拒绝路径形（`C:\...`）→ 前台是
  桌面/shell 早退（"桌面上 Ctrl+C 只会复制剪贴板里原有的东西"）。全链单测。
- **徽章**：BadgeWindow 现有文案即"译 翻译这段"，划词模式下点徽标 = 翻译选中。
- **默认关闭**：设置·划词里开关 + 说明全局鼠标钩子的开销；与现有热键路径并存
  （不替用户决定钩子成本）。

**价值:** 触发方式从"记忆热键"变为"顺手就翻"——Glossy 验证过的阅读场景交互。

**Blocked by:** 35（单词卡成型后才能定徽章点击后的呈现形态）。

**Status:** done (succession via v3 - see docs/manual-test-v5.md)

## 实现记录（2026-09-27，DAG 执行）

- 交付：候选 `054202f`（基线 27f86af，2 提交；前任代理死于配额未留工作，本
  候选为全新执行），tree `6ab1eae`；已晋升 `dag/integration`。17 文件：Core
  `SelectionDrag`（手势状态机）+ `SelectionBadgeFilter`（过滤链）纯逻辑 +
  `Shiyu.Windows/MouseDragHook`（WH_MOUSE_LL 观察者，照 WinVHook 纪律：回调
  只上报）+ `DesktopShell`（桌面早退）+ SelectionCapture 延迟还原变体（面板
  显示后再归还，Glossy 次序细节）+ Badge/Panel/App 接线 + 设置开关（默认关
  +钩子开销说明文案）。
- 门禁：构建 0 错误；**665/665**（基线 632+33：手势 10/过滤链 14/延迟还原
  8/设置钉子 1），TDD 先 RED 后 GREEN；保护文件与基线字节一致。
- 评审：提交前自查修复 2 个正确性缺陷（挂账时序、还原踩用户新复制）；判断项
  4 条处置（2 修 2 接受）。记录：
  `E:\Project\guodapro-dag\candidate-records\ticket-37.md`（仓库外）。
- **协调者真机 E2E 探针（五轮环境攻坚后全过）**：设置 UIA（TabItem 导航到
  快捷键页→勾选徽标开关→点保存）→ 记事本文档区注入拖选 → **「译」徽标浮现
  于拖选终点旁**（实测 rect 202x56）→ **点击徽标→翻译面板打开**。结束后开关
  已复原默认关并保存（用户状态无残留）。攻坚记录：拖选落点须取 UIA Document
  元素矩形首行（窗口矩形中腰拖空=正确地无徽标）；设置页复选框无 UIA 名、按
  TabItem 序数+页内复选框序数定位；保存=倒数第二钮（末钮是关闭）。
- 残余竞态（目标应用多格式发布迟到可入历史）与既有热键取词路径同级，记录在案。

- [x] WH_MOUSE_LL 拖选手势（≥5px）检测 + UI 线程分类（钩子纪律照 WinVHook）
- [x] 取词走 SelectionCapture + 还原时序调整（面板显示后再还原）
- [x] 过滤链全项 + 单测（路径拒绝/最小长度/桌面早退/须含字母）
- [x] BadgeWindow 划词模式接线 + 点击翻译
- [x] 设置开关（默认关）+ 全局钩子开销说明文案
- [x] 全量测试绿；划词 E2E 探针（模拟拖选 → 徽标出现 → 点击 → 面板翻译）

**Acceptance succession (2026-10-03, ticket 28):** closed via v3 - machine-testable parts are covered by the probe suite (28 checks, 0 fail) and 938+3 automated tests; human-judgement items live in docs/manual-test-v5.md.
