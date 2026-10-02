# 09 — 发布流水线与更新验签

**来源：** 优化报告 O-09、O-10；ADR-0010
**Blocked by:** 03（已完成）
**Branch:** `v3/release`（已并 master，3ed19b7）
**Status:** done

实施记录（2026-10-02）：验签进 Core `UpdateSignature`（aed9f8b）；编排下沉 `UpdateOrchestrator`、删 fail-open（60afeca）；App 薄壳/更新窗单例/点击保护/InformationalVersion 显示（58e6ce0）；release.yml + 本地演练测试（664d1b3）。694/694 双配置全绿。

**验收全通过（2026-10-02）**：
- 端到端篡改包拒绝（探针实例 + 本地伪发布源）：显示"更新包签名不符，已拒绝安装。 原版本不受影响。"，暂存区不残留
- **首个真实发布**：tag `v0.9.0-rc1` → release workflow 2m17s 成功，GitHub Release"拾语 0.9.0-rc1"（prerelease）带三件套（zip 65,035,461 字节 / .sha256 / .sig）
- 独立复核：下载回本机，用离线密钥对的公钥 `openssl dgst` 验签 **Verified OK**，SHA-256 匹配；解压 261 文件，`FileVersion=0.9.0.0`（比较用）、`ProductVersion=0.9.0-rc1+<hash>`（关于页显示、剥 hash）
- 首次在用户机安装运行：**用户已下载试装 rc1 并验收（2026-10-02）**

**What to build:**
- `release.yml`：推送 `v*` tag 时，发布 self-contained 的 win-x64 包，版本号取自 tag；生成 zip、`.sha256`、`.sig`（ECDSA P-256，私钥来自 Actions secret）；创建 GitHub Release。
- 客户端内置公钥，先验签再解压；缺签名、签名不符、取不到签名一律拒绝并清空暂存区；删除校验和 fail-open。
- 更新的下载、校验、应用编排下沉到 Core（`UpdateOrchestrator`），可单测。
- 更新窗做成单例；"安装"点击包上 try。
- 签名密钥对由主控生成：私钥写入仓库 secret，并在仓库外离线备份；公钥写进代码。

**验收：**
- [x] 单测：签名正确通过；篡改 zip 1 字节被拒；缺签名被拒
- [x] 端到端（2026-10-02 探针实例实机通过）：本地 HTTP 冒充发布源提供 v9.9.9 篡改包（.sha256 正确、.sig 为伪），更新窗"下载并安装"后显示"更新包签名不符，已拒绝安装。 原版本不受影响。"，数据目录 updates 暂存区不存在（清空/未落盘）
- [ ] 推 `v0.9.0-rc1` tag，十分钟内出现带 zip、`.sha256`、`.sig` 的 Release，下载解压可直接运行，关于页显示 0.9.0
