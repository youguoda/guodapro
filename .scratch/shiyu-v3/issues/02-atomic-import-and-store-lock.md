# 02 — 覆盖导入原子化、设置回放回到 UI 线程、存储加锁

**来源：** 优化报告 O-02、O-03、O-15；判断报告 P0-2
**Blocked by:** —
**Branch:** `v3/import`（执行中）
**Status:** ready-for-agent

**What to build:**
- `EntryStore` 所有 public 方法加锁；依赖 `last_insert_rowid()` 的插入全部改为 `INSERT … RETURNING id`。
- 覆盖导入要么全部成功、要么库完全不变：准备阶段（线程池、不持锁）读条目并把原图解压到暂存目录；应用阶段一个事务；提交后才移入原图，失败删暂存。
- `BackupArchive.Import` 不再接收设置回调，改为在结果里返回 `SettingsJson`；`BackupUi` 回到 UI 线程后再恢复设置；恢复前先解析校验，失败不写盘。
- 失败文案说真话：库层面失败即未改动；设置这一步失败要说"条目已导入、设置未恢复"。

**验收：**
- [ ] 并发压力测试：4 线程混合约 1,000 次读写零异常、id 全对
- [ ] 中途损坏的备份覆盖导入后，条目数、分组数、内容与导入前完全一致，图片目录无新增文件
- [ ] `Import` 返回 `SettingsJson`、不调用回调；`AppSettings.TryParse` 单测
- [ ] 实机（隔离数据目录）：设置窗开着时覆盖导入，主题、热键、置顶按备份生效，无错误弹窗
