# 09 — 发布流水线与更新验签

**来源：** 优化报告 O-09、O-10；ADR-0010
**Blocked by:** 03
**Status:** ready-for-agent

**What to build:**
- `release.yml`：推送 `v*` tag 时，发布 self-contained 的 win-x64 包，版本号取自 tag；生成 zip、`.sha256`、`.sig`（ECDSA P-256，私钥来自 Actions secret）；创建 GitHub Release。
- 客户端内置公钥，先验签再解压；缺签名、签名不符、取不到签名一律拒绝并清空暂存区；删除校验和 fail-open。
- 更新的下载、校验、应用编排下沉到 Core（`UpdateOrchestrator`），可单测。
- 更新窗做成单例；"安装"点击包上 try。
- 签名密钥对由主控生成：私钥写入仓库 secret，并在仓库外离线备份；公钥写进代码。

**验收：**
- [ ] 单测：签名正确通过；篡改 zip 1 字节被拒；缺签名被拒
- [ ] 端到端：本地 HTTP 冒充发布源提供篡改包，更新窗报"签名不符，已拒绝安装"，暂存区被清空
- [ ] 推 `v0.9.0-rc1` tag，十分钟内出现带 zip、`.sha256`、`.sig` 的 Release，下载解压可直接运行，关于页显示 0.9.0
