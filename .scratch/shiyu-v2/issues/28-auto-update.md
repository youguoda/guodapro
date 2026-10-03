# 28 — 自动更新

**What to build:** 拾语能检查新版本、下载并安装，全过程在一个独立窗口中显示
版本说明与进度。可选择自动检查或手动检查。

## 对常驻工具，更新通道是基础设施

一个开机自启、常年运行的工具，如果更新要靠用户自己去下载页面，
那实际结果就是绝大多数用户**永远停在安装时的那个版本**——
包括那些修了数据丢失问题的版本。

## 必须守住的两条

- **更新不能丢数据。** 安装新版本前后，历史、图片、设置必须完整。
  数据库 schema 若有变更，走既有的只增不改的迁移路径。
- **更新失败必须可恢复。** 下载中断、安装被安全软件拦截、版本回滚——
  这些情况下用户应当仍然拥有一个能启动的拾语。

**Blocked by:** 24

**Status:** done (succession via v3 - see docs/manual-test-v5.md)

## 实现记录（2026-09-26，更新通道定为 GitHub Releases）

- **通道**：`UpdateChannel.Default = ("guodapro", "shiyu", "shiyu-win-x64.zip")`——
  一个常量，换仓库/资产名改一行。发布 JSON 走 `releases/latest`；`SHIYU_UPDATE_API`
  环境变量可换 API 基址（探针/自建镜像用）。
- **Core（纯逻辑，25 个新测试）**：`UpdateVersion`（v 前缀/缺位容忍、按位比较）；
  `ReleaseManifest`（tag/notes/assets 解析，缺资产=无可安装而非报错）；
  `UpdateStaging`（校验三件套：字节大小=发布页、SHA256（有则必验）、zip 可开且含
  Shiyu.App.exe；失败即 `Reset` 清整个 updates 目录——不留半个安装包）；
  `UpdateApply`（终替：等旧进程退出→备份安装目录→覆盖→**任何一步失败从备份还原，
  原版本可启动**；只增不改，不删旧文件）。
- **App**：`UpdateService`（下载带进度到 `.partial`，完成才转正；校验、解压、
  写 pending）；`UpdateWindow`（当前/最新版本、说明、进度条、失败句子）；
  托盘菜单「检查更新…」手动入口；`about.update-auto` 开关（默认开，启动 5s 后
  静默查一次，**发现新版只弹托盘提醒，安装永远亲手点**）。
- **两阶段交换**（探针逼出三个真 bug，均已修+复测）：
  1. 运行中的进程换不了自己的文件 → 新副本以 `--finalize-update <旧pid> <安装目录>
     <备份> <数据目录>` 启动，等旧进程退出后换。
  2. **`AppContext.BaseDirectory` 尾随反斜杠会转义参数里的闭引号**，argv 全乱、
     终替进程当成完整应用启动——路径一律 `TrimEnd('\\')`。
  3. 终替成功后清理 staging 会删掉**自己正在运行的 exe**，随后 `OnExit` JIT 加载
     Shiyyu.Windows 即崩 → 终替路径用 `Environment.Exit(0)` 跳过 OnExit；staged
     目录删不掉的部分交给**重启后的新版本**在启动时清理（`CleanStagedIfIdle`）。
- **数据不动**：交换只碰安装目录，数据目录（历史/图片/设置）零接触；探针实测
  更新前后数据库与其中条目原样。schema 变更由新版本首次 `EntryStore.Open` 走既有
  只增迁移（本迭代 v11 即走过该路径的活例子）。
- **监听不受扰**：下载全在后台；交换发生在旧进程退出之后。
- **探针**（`%TEMP%\upb.ps1`，14/14）：本地 HttpListener 冒充 GitHub Releases，
  v99.0.0 假发布+真 zip+SHA256；隔离数据目录（`SHIYU_DATA_DIR`）；UIA 驱动更新窗
  全流程：检查→下载校验→staged→无 .partial→pending→安装目录被替换→备份清理→
  新版本自安装目录重启→历史条目健在→重启后 staging 清理。回滚路径由 Core 测试
  覆盖（只读文件拦截→还原→原版本可启动）。
- 探针辅助钩子：`SHIYU_DATA_DIR`（隔离数据目录）、`SHIYU_UPDATE_API`、
  `SHIYU_OPEN_UPDATE`（启动即开更新窗）——与既有 `SHIYU_OPEN_SETTINGS` 同族，
  仅探测用，不设即无行为差异。

- [x] 可手动检查更新，也可配置为自动检查
- [x] 独立窗口显示版本号、更新说明与下载进度
- [x] 下载中断或校验失败时给出明确提示，不留下半个安装包
- [x] **更新前后历史、图片、标签、分组、设置完整无损**
- [x] schema 变更走只增不改的迁移，旧数据不丢
- [x] 安装被拦截或失败时，原版本仍可正常启动
- [x] 更新过程不干扰正在进行的剪贴板监听

**Acceptance succession (2026-10-03, ticket 28):** closed via v3 - machine-testable parts are covered by the probe suite (28 checks, 0 fail) and 938+3 automated tests; human-judgement items live in docs/manual-test-v5.md.
