# 36 — 战略级一期：免费零配置翻译公共通道（RelayBackend + Worker）

**What to build:** 落实 Glossy 调研（docs/research/2026-09-27-glossy-analysis.md
§9.1 一期）——"客户端零密钥"的公共翻译通道，解决拾语最大短板：新手必须填
DeepSeek key 才能用翻译，没 key 则翻译功能等于不存在。

- **服务端**：Cloudflare Worker，参考实现按拾语纪律重写（TypeScript + 测试，
  不直接拷贝 Glossy 的 ~70KB JS 以保持工程一致性）。上游只接智谱 glm-4-flash
  免费档（OpenAI 兼容）；密钥仅存 Worker 环境变量，客户端零密钥。
- **额度**（照抄 Glossy 实证参数）：设备 2 万字/天 + IP 3 万字/天（加盐哈希
  存储）+ 全局封顶 + 单请求 2k 字；上游失败退还已计字符；配额错误带剩余量与
  重置时间。
- **客户端**：Core 加第三种后端 `RelayBackend`（与 OpenAiCompatibleBackend 同
  接口；上游非流式，返回后按句切分模拟流式吐出，保持面板流式观感）。
- **设置**：后端类型加"公共通道（免费额度）"选项；隐私披露明示"被翻译的
  文本会经中转发给模型服务；剪贴板历史本身仍不出机器"。
- **引导**：公共通道列为默认路径，自备密钥降为高级可选。
- **二期默认化不在本票**：数据验证后另行开票。

**价值:** 翻译激活率从"填了 key 的人才用"变为"开箱即用"——与 Glossy 差距表
里的最大短板。

**Blocked by:** 34（错误码映射与退避是通道健壮性的地基）。

**Status:** ready-for-agent

- [ ] Worker：/translate 端点（clientId+text+from+to）+ 智谱上游 + 四层配额
- [ ] Worker：失败退款、IP 加盐哈希、配额错误码带剩余量；wrangler/vitest 测试
- [ ] Core：RelayBackend（同接口、模拟流式）+ 假上游单测
- [ ] 设置：后端第三选项"公共通道（免费额度）"+ 隐私披露文案
- [ ] 引导：公共通道默认路径，凭据步骤改可选高级项
- [ ] 端到端：真 Worker 部署 + 客户端翻译探针 + 配额耗尽人话报错探针
- [ ] docs：wrangler.toml 模板 + secrets 清单 + 部署步骤
