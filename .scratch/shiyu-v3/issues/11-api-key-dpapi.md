# 11 — 密钥 DPAPI 保护，备份默认不含密钥

**来源：** 优化报告 O-18；ADR-0011
**Blocked by:** 02、07（均已完成）
**Branch:** `v3/dpapi`（已并 master，b7b9cab）
**Status:** done

实施+验收记录（2026-10-02，主控亲做）：Core `ISecretProtector` 端口 + `AppSettings.SecretProtector` 环境安装；Save 落盘保护（保护失败拒绝写入而非裸奔）、TryParse 还原（打不开即清空，绝不把密文当凭据发出去）、旧明文加载后下次保存自动迁移；`ToBackupJson(includeKey)` 备份副本默认不含密钥。Windows `DpapiSecretProtector`（CurrentUser + 专属熵，零新依赖）。App：启动先装保护器；备份导出口令对话框（加密时）多一枚"同时导出 API 密钥"复选框；导入恢复时备份无密钥则保留本机现有密钥（导入历史≠注销翻译服务）。5 新测试（775 全绿）。**实机验收（探针实例）通过**：设置窗密钥框输入 → 保存 → settings.json 仅 `dpapi:` 密文、无明文残留。

**What to build:**
- 设置文件里的 API 密钥改为 DPAPI（`CurrentUser`）保护后的 Base64；首次加载自动迁移旧明文。
- 备份默认不含密钥；"加密导出"时可选"同时导出 API 密钥"（导出时写入加密备份内部的明文，导入时用本机 DPAPI 重新保护）。

**验收：**
- [ ] 设置文件里搜不到密钥原文
- [ ] 不加密导出的备份解压后搜不到密钥
- [ ] 升级后原有翻译功能照常可用（迁移单测 + 实机）
