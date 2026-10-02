# 09 — 发布流水线与更新验签

**来源：** 优化报告 O-09、O-10；ADR-0010
**Blocked by:** 03（已完成）
**Branch:** `v3/release`（已并 master，3ed19b7）
**Status:** ready-for-human

实施记录（2026-10-02）：验签进 Core `UpdateSignature`（aed9f8b）；编排下沉 `UpdateOrchestrator`、删 fail-open（60afeca）；App 薄壳/更新窗单例/点击保护/InformationalVersion 显示（58e6ce0）；release.yml + 本地演练测试（664d1b3，测试钥实跑 publish→打包→签名→VerifyInstaller/Extract/验签全链）。694/694 双配置全绿。主控已核对：workflow 公钥 == 内置公钥 == 本机密钥对；secret 已就位；分支保护已开。

**What to build:**
- `release.yml`：推送 `v*` tag 时，发布 self-contained 的 win-x64 包，版本号取自 tag；生成 zip、`.sha256`、`.sig`（ECDSA P-256，私钥来自 Actions secret）；创建 GitHub Release。
- 客户端内置公钥，先验签再解压；缺签名、签名不符、取不到签名一律拒绝并清空暂存区；删除校验和 fail-open。
- 更新的下载、校验、应用编排下沉到 Core（`UpdateOrchestrator`），可单测。
- 更新窗做成单例；"安装"点击包上 try。
- 签名密钥对由主控生成：私钥写入仓库 secret，并在仓库外离线备份；公钥写进代码。

**验收：**
- [x] 单测：签名正确通过；篡改 zip 1 字节被拒；缺签名被拒
- [ ] 端到端：本地 HTTP 冒充发布源提供篡改包，更新窗报"签名不符，已拒绝安装"，暂存区被清空（合入票 15 探针后实机执行）
- [ ] 推 `v0.9.0-rc1` tag，十分钟内出现带 zip、`.sha256`、`.sig` 的 Release，下载解压可直接运行，关于页显示 0.9.0
