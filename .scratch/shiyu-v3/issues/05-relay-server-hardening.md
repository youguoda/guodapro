# 05 — 公共中转服务端加固（部署前必做）

**来源：** 优化报告 O-19；ADR-0009
**Blocked by:** —
**Branch:** `v3/server`（执行中）
**Status:** ready-for-agent

**What to build:**
- `to` / `from` 改为语言白名单，规范化后再进提示词；不在名单里返回 400。
- 计费覆盖所有进入提示词、由用户控制的内容。
- 每 IP（IPv6 按 /64 聚合）突发限速，超限 429 + `Retry-After`。
- 三层额度（设备、IP、全局）用 Durable Object 原子"检查并预留"。
- `IP_HASH_SALT` 缺失时 fail closed（503）。
- `server/README.md`：新绑定与变量、部署前检查清单、已知限制（clientId 可伪造；`*.workers.dev` 在大陆不可达，必须绑自定义域名）。

**验收：**
- [ ] vitest 全绿，覆盖：超长 / 非法 `to` 返回 400、别名规范化、IPv6 /64 共享额度、429、三层原子语义、缺盐 503
- [ ] 不部署；不新增任何记录正文的日志
