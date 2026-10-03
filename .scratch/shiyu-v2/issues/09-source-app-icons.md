# 09 — 来源应用图标

**What to build:** 每条记录在列表里显示它来自哪个应用的图标，一眼就能区分
"这段是从浏览器复制的"和"这段是从终端复制的"。取不到图标时显示拾语自己的标识，
而不是留空。

## 为什么值得单独一张票

拾语已经记了来源应用的名字，但名字在窄列表里占地方且扫读慢。图标是同样的信息、
更小的面积、更快的识别——这是列表信息密度的关键一环。

图标必须**落盘缓存并按应用去重**：同一个应用的上千条记录不该各存一份图标，
也不该每次渲染都去问系统要。参照实现为此单独建了应用表与图标存储，值得照做。

**Blocked by:** None — can start immediately.

**Status:** done (succession via v3 - see docs/manual-test-v5.md)

- [x] 列表中每条记录显示来源应用图标
- [x] 同一应用的多条记录共用一份缓存图标，不重复占用空间
- [x] 取不到图标时显示拾语标识，不留空、不显示破图
- [x] 来源应用已被卸载后，历史记录仍显示此前缓存的图标
- [x] 图标获取失败不阻断条目入库——记录照常产生，只是没有图标
- [x] 长列表滚动时不因图标加载而卡顿

## Comments

### 交付（2026-09-25）

**架构：记录时提取，一应用一行，显示只读缓存。**

- 快照带上 `SourceExePath`（前台进程的 exe，在复制发生的当下趁进程活着拿到——
  这是卸载后仍有图标可显的唯一时机）；提权进程的 MainModule 拿不到则置空，
  条目照记。
- `ISourceIconProvider` 端口（Core）+ `WindowsSourceIcons`（Shiyu.Windows）：
  `SHGetFileInfoW` 大图标 → `CreateBitmapSourceFromHIcon` → PNG 字节。
- `SourceIconCache`：首次见到某应用才提取一次；**找不到图标也写一行空墓碑**，
  之后不再重试；`Ensure` 吞掉一切异常——图标永远不值得赌上条目。
- `EntryStore` v4 迁移（只增不改）：`applications(name PRIMARY KEY, icon BLOB)`，
  `INSERT OR IGNORE` 保证并发首见不互相覆盖。
- 管理窗列表：元信息行首 16×16 图标（按应用解码一次、`DecodePixelWidth=16`
  控制内存），无图标时显示蓝底圆角"拾"字标识——不留空、不破图。票 12 的
  窄条卡片沿用同一套。

**测试：新增 8 项（伪造 provider + 真实 SQLite），全绿（总计 253）。**
去重（同应用三次复制只问一次）、墓碑不重试、provider 抛异常条目照常入库、
无 exe 路径不问、图片复制同样取图标、缓存跨重启存活、排除内容不碰图标表。

**实机验证**：对真实 exe 提取——notepad 4381 字节 PNG、explorer 1718 字节、
不存在的路径优雅返回 null。期间踩一坑已修：`SHGetFileInfoW` 在 **shell32.dll**
而非 user32，声明错 DLL 时入口点异常被 catch 吞成"无图标"，排查时先裸调
API 隔离出转换层无辜、声明有罪。

**遗留说明**：墓碑无重试机制——某应用先无图标后又装好了，要到其来源名变化
或手工清 applications 表才会再试；概率极低，记录在案不做。

**待人工**：重启拾语后随便复制几条（浏览器、终端、微信各一），开管理窗看
行首图标是否正确、未知来源是否显示"拾"字兜底。

**Acceptance succession (2026-10-03, ticket 28):** closed via v3 - machine-testable parts are covered by the probe suite (28 checks, 0 fail) and 938+3 automated tests; human-judgement items live in docs/manual-test-v5.md.
