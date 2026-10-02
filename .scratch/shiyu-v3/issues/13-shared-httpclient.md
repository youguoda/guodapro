# 13 — 共享 HttpClient 与超时策略

**来源：** 优化报告 O-23；审计 A §2
**Blocked by:** 08（已完成）
**Branch:** `v3/http`（已并 master，9d96a81）
**Status:** done

实施记录（2026-10-02，主控亲做）：`HttpClients.Shared`（SocketsHttpHandler 连接池 5 分钟）注入全部五个出口（面板/词典/中转/测试连接/更新下载）；流式超时拆为**首字节 15s + 块间空闲 20s**（`ChunkTimeoutStream` 每次读各自计时），删掉 30s 总时长一刀切与 `TranslationBackendOptions.Timeout`；预算外壳与用户取消仍管全程；更新编排 UA 改按请求头；ConnectionProbe 自带 15s（可注入）。5 新测试（780 全绿），顺带消除 SecretProtector 环境静态量的测试并行竞态（settings-io Collection）。

**验收：**
- [x] 单测：慢而不断的流超过首字节预算仍流完（1.2s vs 300ms）；首字节超时；块间卡死超时；共享客户端不被借用方 Dispose 关闭；调用方预算取消全程有效
- [ ] 实机：连续翻译两次，第二次首字时间明显下降（需配置真实后端；随 Release 切换或用户日常使用观察）
- [x] 一段 2,000 字的流式翻译不再在 30 秒处被截断（由"总时长不再设上限"的结构性保证 + 慢流单测钉住）

**What to build:**
- 进程级共享一个 `HttpClient`（`SocketsHttpHandler`，`PooledConnectionLifetime` 5 分钟），通过各类现有的可选构造参数注入（翻译后端、中转、免费词典、Agent、更新）。
- 超时拆成"首字节超时"（如 15 秒）和"相邻数据块之间的空闲超时"（如 20 秒），不再限制流的总时长（现在长文本在 30 秒处被截断）。

**验收：**
- [ ] 单测：慢速但持续出数据的流超过 30 秒不被截断；首字节超时、空闲超时各自生效
- [ ] 实机：连续翻译两次，第二次首字时间明显下降（本地计时，日志只记毫秒数）
