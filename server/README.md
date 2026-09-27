# 拾语公共翻译通道（shiyu-relay）

票 36 的服务端：一个 Cloudflare Worker，让拾语用户**零配置、零密钥**地用上
翻译——上游只接智谱 `glm-4-flash` 免费档（OpenAI 兼容），凭据只活在
Worker 的 secret 里，客户端永远碰不到。

架构与配额参数照抄 Glossy 的实证设计（`docs/research/2026-09-27-glossy-analysis.md`
§4.4/§9.1），代码按拾语纪律用 TypeScript 重写并配 vitest 测试。

## 目录

```
server/
  src/
    index.ts     # fetch 入口：只有 POST /translate，其余 404
    handler.ts   # 编排：校验 → 配额预留 → 上游 → 失败退款
    quota.ts     # 四层配额：设备/IP/全局/单请求，含退款
    hash.ts      # IP 加盐哈希（SHA-256，盐走 secret）
    upstream.ts  # 智谱 glm-4-flash 非流式调用（OpenAI 兼容形状）
    prompt.ts    # 翻译指令（与客户端 TranslationPrompt 同族，压制加戏）
    limits.ts    # 限额读取（vars 可覆盖，默认即实证参数）
    types.ts     # Env / 端口接口
  test/          # vitest：内存 KV + 可注入上游，全程不碰网络
```

## 契约

### 请求

```
POST {endpoint}/translate
Content-Type: application/json

{"clientId":"<8-64 位 [A-Za-z0-9-]，匿名安装 ID>","text":"...","from":"English 或 null","to":"Chinese"}
```

`from` 为 null 表示自动检测。单请求上限 2000 字（按码点数计）。

### 成功

```
200 {"translation":"整段译文"}
```

上游非流式、中转一次回完；客户端 `RelayBackend` 自己按句切分模拟流式。

### 错误（统一形状，message 一律人话中文）

| 状态 | code | 附加字段 | 含义 |
|---|---|---|---|
| 400 | `BAD_REQUEST` | | 请求体/字段不合法 |
| 400 | `TEXT_TOO_LONG` | `limit` | 超过单请求上限 |
| 429 | `QUOTA_DEVICE` / `QUOTA_IP` / `QUOTA_GLOBAL` | `remaining`、`resetAt` | 对应层日配额用尽 |
| 502 | `UPSTREAM_ERROR` | | 上游失败（已退还本次计费） |
| 405 | `METHOD_NOT_ALLOWED` | | 非 POST |
| 404 | `NOT_FOUND` | | 路径不存在 |

配额错误的 `resetAt` 是下一个 UTC 零点（ISO 8601）。

## 配额（Glossy 实证参数）

| 层 | 默认 | 说明 |
|---|---|---|
| 设备 | 20000 字/天 | 按 clientId（匿名安装 ID） |
| IP | 30000 字/天 | 按 `CF-Connecting-IP` 的加盐哈希——**原始 IP 从不落 KV** |
| 全局 | 200000 字/天 | 部署者的总闸，vars 里随流量调 |
| 单请求 | 2000 字 | 客户端也有同款前置检查 |

- 计费单位是源文本的**码点数**，UTC 00:00 重置（桶键带日期，TTL 2 天自清）。
- **失败退款**：上游没给译文就把已计字符全额退还，用户不替通道的故障买单。
- 拒绝不记账：被拒的请求不占额度。

## Secrets 清单（绝不进仓库）

```bash
npx wrangler secret put ZHIPU_API_KEY   # 智谱开放平台的 key
npx wrangler secret put IP_HASH_SALT    # 随机长串；换盐=全体 IP 身份重置
```

本地开发把同样的键放进 `server/.dev.vars`（已被 .gitignore 排除）。

生成盐的示例：`node -e "console.log(require('crypto').randomBytes(32).toString('hex'))"`

## 部署步骤

前置：Node 18+、一个 Cloudflare 账号（Workers 免费档即可）。

```bash
cd server
npm install
npx wrangler login                       # 浏览器授权
npx wrangler kv namespace create QUOTA   # 把输出里的 id 填进 wrangler.toml
npx wrangler secret put ZHIPU_API_KEY
npx wrangler secret put IP_HASH_SALT
npm test                                 # 本地门禁：内存 KV + 假上游，37 例
npx wrangler deploy                      # → 得到 https://shiyu-relay.<你的子域>.workers.dev
```

部署后冒烟（把 URL 换成你的）：

```bash
curl -sS https://shiyu-relay.example.workers.dev/translate \
  -H 'content-type: application/json' \
  -d '{"clientId":"smoke-test-0001","text":"Hello there.","from":null,"to":"Chinese"}'
# 期望 {"translation":"…"}；再发 2001 字文本期望 TEXT_TOO_LONG；
# 用掉设备额度后期望 429 且带 remaining/resetAt。
```

### 与客户端配对

客户端默认端点是 `RelayBackend.DefaultEndpoint`
（`src/Shiyu.Core/RelayBackend.cs`），**三处必须一起改**：该常量、
`AppSettings.RelayEndpoint` 的默认值、本文档。自建/测试时无需改代码：
启动拾语前设 `SHIYU_RELAY_URL=http://127.0.0.1:8787` 即指向
`npx wrangler dev` 起的本地 Worker（探针约定，与 `SHIYU_DATA_DIR` 同族，
只影响本次运行、不写设置文件）。

## 已知限制（一期明知的取舍）

- **KV 最终一致性**：并发下的计数可能偏松（多放几个字），这是免费通道
  选 KV 的代价；要精确可换 Durable Objects。
- **clientId 可伪造**：它是配额身份不是鉴权（Glossy 作者同样承认）。
  伪造者绕过的只是每天 2 万字的份，配合 IP 层与全局封顶可兜底。
- **无鉴权**：知道 URL 的人都能用，配额是唯一的闸。URL 泄露=额度被
  白嫖，必要时轮换 Worker 名或加一层共享口令（二期再说）。
- **上游单点**：只接智谱免费档；它挂了通道就挂（客户端有退避重试与
  人话报错）。多上游级联是二期候选。

## 本地开发

```bash
npm test          # vitest run
npm run typecheck # tsc --noEmit
npm run dev       # wrangler dev（需 .dev.vars 里有两个 secret）
```
