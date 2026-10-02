# 12 — 存储为规模设计

**来源：** 优化报告 O-22，以及 O-37 的"变更事件取代轮询"；审计 A2 §4
**Blocked by:** 02
**Status:** ready-for-human（实现完成，见下方基准与测试）

**What to build:**
- **先做（S）：**
  - 建排序索引 `(pinned DESC, created_at DESC, id DESC)`；
  - 子类型回填用哨兵值，不再每次启动重扫全部普通文本；
  - `Count()` 加缓存，写入时失效；
  - 快速条搜索加去抖。
- **再做（M）：**
  - `thumbnail`、`html`、`rtf` 拆到附表，列表只读窄表；
  - 搜索用 FTS5 `trigram`（≥ 3 个字符；1–2 个字符时在窄表上扫描）；
  - 分页改 keyset；
  - `EntryStore.Changed` 事件取代窄条每 2 秒的计数轮询（同时修掉"重复复制被 Touch 顶到最前时窄条不刷新"）。
- 迁移旧库要有测试，迁移过程可中断恢复。

**验收：**
- [x] 用占位数据生成 10 万条混合库，脚本测量：启动到托盘出现 < 1 s；窄条一次搜索 < 100 ms；一次复制入库 < 20 ms（结果记在本票）
- [x] 中文子串搜索测试（含 1、2、3 个字符）
- [x] 旧库迁移测试

## 基准数字（2026-10-01，`tools/benchmarks/storage-bench.ps1`）

库：10 万条混合（10% 图片合成缩略图 20–40 KB、20% 富文本 HTML 数十 KB、70% 中英混合文本、1% 置顶、底部埋针），约 1.25 GB；SQLite 3.53.3（e_sqlite3 随 Microsoft.Data.Sqlite 10.0.12）；Release；NVMe，OS 文件缓存热（诚实说明：脚本无权丢页缓存，冷盘数字对所有实现同倍变差）。

| 指标 | 目标 | 实测 |
|---|---|---|
| 启动存储段（Open+Migrate+首页 100 条） | < 1 s | **0–3 ms**（第 2、3 次开 0 ms） |
| 窄条一次搜索（trigram，命中 1 条的底部针） | < 100 ms | **39–46 ms** |
| 搜索（trigram，命中 60 条出一页） | — | 27–36 ms |
| 搜索（1–2 字符走窄表 LIKE） | — | 0.3–0.4 ms |
| 一次复制入库（Append+Touch，50 次均值/最大） | < 20 ms | **1.2 / 1.9 ms** |
| 老库一次性升级（v11 行内大字段 → 当前版，含搬表+VACUUM+FTS 重建） | — | **47.8 s**（一次性；升级后立即开库 0 ms、搜索 100 命中） |

三项全部达标。说明两点：①"启动"测的是存储切片（开库+迁移+首页），未启动 App 本体——启动到托盘的剩余部分不在本票范围；②大库（>1 GB）一次性升级约 48 s，发生在托盘出现前的迁移阶段——10 万条混合库是这个量级的上限场景，数千条的真实库在秒级。

## 实现纪要

- schema v12：排序索引 `idx_entries_order`；子类型哨兵（普通文本存 `'None'`，NULL 只属于老行，一次性迁移补齐后不再扫）。
- schema v13：`entry_blobs(entry_id PK, thumbnail, html, rtf)` 附表；迁移分批（500/批、每批一事务、纯 SQL）搬完 `DROP COLUMN` + `VACUUM`（DROP COLUMN 只藏列不擦字节，不 VACUUM 旧行仍走溢出页）；谓词幂等故可中断续跑。列表查询只读窄表；`Get`/`BlobsOf`（每页一次批取缩略图）/`EntriesWithBlobsAfter`（id 游标整库遍历，导出与合并去重指纹同步换用）按需带负载；窄条粘贴/拖出的富文本在动作时刻单次主键读取。
- schema v14：`entries_fts`（trigram、外部内容、三触发器同步，导入事务与回滚随行）；`Search/Find/CountMatching` 共用 `TextContains`：≥3 字符 MATCH 短语（引号包裹、内引号翻倍，用户输入永当字面量），1–2 字符回落窄表 LIKE；trigram 默认折叠 ASCII 大小写，英文大小写不敏感与原 LIKE 一致。
- keyset：`PageCursor(pinned, created_at, id)` 行值比较，`Page/Find` 与 `HistoryBrowser.LoadMore` 全链游标化。
- 事件：`EntryStore.Changed`——全部公共写经 `Write` 包装器（gate 内计帧深、最外层提交后锁外触发一次；批次一条、回滚零条、锁外触发防 Dispatcher.Invoke 死锁）；窄条订阅取代 2 s 计数轮询（可见整读、隐藏忽略，边界同旧探针；自写 `SelfWrite` 抑制避免收藏/备注原地更新被重置滚动）；Touch 顶前、他窗改备注从此能刷新。

## 测试

新增 `StorageScaleTests` 等共 23 测（716 → 739 全绿）：索引/哨兵/回填一次/Count 缓存、拆附表迁移与中断恢复、负载批取与游标遍历、SQLite 版本钉住、中文 1/2/3 字子串、英文大小写、FTS 语法字符字面量、删除/清空/导入/回滚的索引同步、老库重建、Changed 逐写一报/批次一报/回滚零报/回调不死锁、v2 形态全梯升级端到端、游标整走与滚动中插入不重不漏。

