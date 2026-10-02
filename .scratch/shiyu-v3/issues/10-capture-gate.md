# 10 — 排除名单统一闸口

**来源：** 优化报告 O-17；ADR-0007"排除规则成为唯一防线"
**Blocked by:** 04（已完成）
**Branch:** `v3/gate`（已并 master，4b1963c）
**Status:** ready-for-human（余实机探针走查）

实施记录（2026-10-02，主控亲做）：Core `CaptureGate`（前台判定 + 条目拆分，SourceApp 匹配语义与 ExclusionPolicy 完全一致——contains、大小写不敏感；内容规则不适用于"还没有内容"的取词路径）；`ForegroundApplication.Current()`（ShiYu.Windows，监视器去重）。接线：**划词徽标**在模拟 Ctrl+C 之前查前台（被动遭遇，静默不出徽标）；**划词热键**同样先查（显式请求，托盘提示一次）；**Agent 动作**按条目 SourceApp 过 `SplitSendable` 并报告跳过数（EntryItem 补 SourceApp 字段；批量翻译原本就在 RunAsync 内复查排除规则）；**历史记录**路径原有排除不动。翻译剪贴板是用户对自己剪贴板的显式命令，视为同意，不过闸口（票面记录该取舍）。5 新测试，770 全绿。

**What to build:**
- Core 里唯一的判定点 `CaptureGate.Allows(source)`。所有"读取别人内容"的入口都先经过它：历史记录管线、划词徽标、热键取词、Agent 动作。
- 划词路径在**模拟 Ctrl+C 之前**按前台进程判定（现在完全不查排除名单，会向密码管理器模拟 Ctrl+C）。
- 热键取词是显式请求，仍允许；前台进程在排除名单里时先提示一次。

**验收：**
- [x] 每个入口都有单测（CaptureGateTests 5 条：前台命中/大小写/空与未知/内容规则不拦取词/条目拆分与计数/全排除空结果）
- [ ] 探针：在排除名单里的进程中拖选文字，剪贴板序列号（`GetClipboardSequenceNumber`）不变，没有徽标（探针模式不装钩子，此条需非探针隔离实例或随票 15 脚本；由用户执行亦可——在 KeePass 类应用里拖选不应出"译"徽标）
