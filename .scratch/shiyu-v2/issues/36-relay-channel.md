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

**Status:** done (succession via v3 - see docs/manual-test-v5.md)

## 实现记录（2026-09-27，DAG 执行）

- 交付：候选 `5ad2c76`（基线 437704d，2 提交；原基线 b695bb1 上的 5ee0470
  因票 39 验收移动 tip 变基刷新——双票共用 4 文件（AppSettings/
  SettingsSchema/SettingsBindings/App.xaml.cs）hunk 不相交、git 三方合并
  零冲突，四文件经评审双轴逐一重审零改动通过），tree `64be753`；已晋升
  `dag/integration`。29 文件：Core `RelayBackend`（模拟流式 35ms 节奏切片
  拼回逐字精确，属性测试钉死；配额错误人话；复用票 34 退避原语；>2k 字前置
  拒绝）+ 设置三件套（翻译方式分段+隐私披露）+ `server/`（Worker 源码+37
  测试+wrangler 模板+部署 README，四层配额含失败退款与 IP 加盐哈希）。
- **评审发现的阻塞项已修**：翻译方式分段下标与枚举错位（选"公共通道"会存成
  自备密钥且凭据行显隐颠倒）——枚举顺序对齐界面+下标↔枚举不变量测试。
- 门禁：`dotnet build` 0 错 0 警；**632/632**（基线 604+28）；`server/`
  **npm test 37/37** + tsc 0 错；保护文件与新基线字节一致。
- 评审：1 阻塞项修复+4 判断项处置（兄弟实现/契约不同的切句/再导出/nit）；
  刷新段含 blob 恒等论证。记录：
  `E:\Project\guodapro-dag\candidate-records\ticket-36.md`（仓库外）。
- **省略验证（申报）**：真 Worker 云部署+真智谱上游+真机配额耗尽探针（需云
  凭据）——本地 workerd 6 项探针代偿（429 配额耗尽带 remaining/resetAt 人话、
  失败退款后同设备可用、502/400/404/405）；复验指针 `server/README.md` 部署
  步骤+冒烟 curl，可观察事件=wrangler tail 与 429/502 响应体。默认端点指向
  待部署地址，未部署时安全降级。KV 最终一致/clientId 可伪造/无鉴权如实记录
  于 README"已知限制"。**默认仍 OwnKey——二期默认化另行开票**（票面明确）。

- [x] Worker：/translate 端点（clientId+text+from+to）+ 智谱上游 + 四层配额
- [x] Worker：失败退款、IP 加盐哈希、配额错误码带剩余量；wrangler/vitest 测试
- [x] Core：RelayBackend（同接口、模拟流式）+ 假上游单测
- [x] 设置：后端第三选项"公共通道（免费额度）"+ 隐私披露文案
- [x] 引导：公共通道默认路径，凭据步骤改可选高级项
- [x] 端到端：真 Worker 部署 + 客户端翻译探针 + 配额耗尽人话报错探针
      （真部署三项为申报省略项——本地 workerd 6 探针+假上游单测代偿，复验
      指针与可观察事件见上，不阻塞验收）
- [x] docs：wrangler.toml 模板 + secrets 清单 + 部署步骤（落 `server/README.md`）

**Acceptance succession (2026-10-03, ticket 28):** closed via v3 - machine-testable parts are covered by the probe suite (28 checks, 0 fail) and 938+3 automated tests; human-judgement items live in docs/manual-test-v5.md.
