# 07 — 设置只有一个写入口（SettingsStore）

**来源：** 优化报告 O-07、O-20；审计 A2 §1（S1–S7）
**Blocked by:** 02
**Status:** ready-for-agent

**What to build:**
- Core 里的 `SettingsStore { Current; Update(Func<AppSettings, AppSettings>); event Changed }`：所有写入都是"在最新值上做增量"，保存后广播。
- 设置窗只把**改过的**项应用到最新值；窄条置顶钉只上报布尔值；启动引导、重跑引导、备份恢复都走同一个应用路径（现在有三份已经分叉的 apply）。
- 所有窗口订阅 `Changed`：面板的译文语言跟随设置；引导里改的热键、主题本次会话立即生效。
- 加载失败时，先把原文件改名为 `settings.json.bad-<时间>` 再写默认值，绝不覆盖。
- `SHIYU_RELAY_URL` 只在内存中生效，不再被写进文件。

**验收：**
- [ ] Core 单测：两个调用方交错修改不同字段，两处修改都保留
- [ ] Core 单测：加载损坏的文件时原文件被改名保留
- [ ] 实机：S1（设置窗开着点置顶钉再保存）、S2（引导期间唤出窄条后点置顶钉）、S3（导入后保存）、S4（重跑引导）都不再回滚
