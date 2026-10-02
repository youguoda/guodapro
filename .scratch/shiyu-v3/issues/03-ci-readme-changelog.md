# 03 — CI、README、CHANGELOG

**来源：** 优化报告 O-08、O-12、O-13
**Blocked by:** —
**Branch:** `v3/ci-docs`（执行中）
**Status:** ready-for-agent

**What to build:**
- `.github/workflows/ci.yml`：Windows 上构建 `Shiyu.sln`（Release）并跑 Core 测试；Ubuntu 上跑 `server/` 的 `npm ci && npm test`。
- 根 `README.md`（中文）：定位、核心能力、隐私承诺、系统要求、安装/自行构建、首次配置翻译（自备密钥）、默认快捷键、开发说明、许可证现状（目前没有 LICENSE，如实写）。
- `CHANGELOG.md`：未发布区记录 v3 修复；v1、v2 按主题概述。
- CI 第一次全绿后：开启 master 分支保护（必须通过 CI；管理员可直接推送）。

**验收：**
- [ ] 推送后 Actions 两个 job 均为绿
- [ ] README 每条事实都能在仓库中找到依据；不含个人数据
- [ ] 分支保护已开启，状态检查名与 workflow 一致
