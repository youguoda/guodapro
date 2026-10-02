# 10 — 排除名单统一闸口

**来源：** 优化报告 O-17；ADR-0007"排除规则成为唯一防线"
**Blocked by:** 04
**Status:** ready-for-agent

**What to build:**
- Core 里唯一的判定点 `CaptureGate.Allows(source)`。所有"读取别人内容"的入口都先经过它：历史记录管线、划词徽标、热键取词、Agent 动作。
- 划词路径在**模拟 Ctrl+C 之前**按前台进程判定（现在完全不查排除名单，会向密码管理器模拟 Ctrl+C）。
- 热键取词是显式请求，仍允许；前台进程在排除名单里时先提示一次。

**验收：**
- [ ] 每个入口都有单测
- [ ] 探针：在排除名单里的进程中拖选文字，剪贴板序列号（`GetClipboardSequenceNumber`）不变，没有徽标
