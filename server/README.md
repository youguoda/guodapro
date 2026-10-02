# 拾语公共翻译通道（shiyu-relay）

票 36 的服务端：一个 Cloudflare Worker，让拾语用户**零配置、零密钥**地用上
翻译——上游只接智谱 `glm-4-flash` 免费档（OpenAI 兼容），凭据只活在
Worker 的 secret 里，客户端永远碰不到。O-19 完成部署前安全加固（语言
白名单、IPv6 /64 聚合、突发限速、Durable Object 原子配额、盐 fail-closed）。

架构与配额参数照抄 Glossy 的实证设计（`docs/research/2026-09-27-glossy-analysis.md`
§4.4/§9.1），代码按拾语纪律用 TypeScript 重写并配 vitest 测试。

## 目录

```
server/
  src/
    index.ts     # fetch 入口：POST /translate、GET /health，其余 404；导出 DailyCounter
    handler.ts   # 编排：校验 → 语言白名单 → 限速/配额原子预留 → 上游 → 失败退款
    quota.ts     # QuotaCounter 端口 + DailyCounter（按日期分片的 Durable Object）+ 适配器
    langs.ts     # 语言白名单：to/from 折叠成规范名（O-19）
    ip.ts        # IP 规范化：IPv6 折叠到 /64 再哈希（O-19）
    hash.ts      # IP 加盐哈希（SHA-256，盐走 secret，缺失即 503）
    upstream.ts  # 智谱 glm-4-flash 非流式调用（OpenAI 兼容形状）
    prompt.ts    # 翻译指令（与客户端 TranslationPrompt 同族，压制加戏）
    limits.ts    # 限额读取（vars 可覆盖，默认即实证参数）
    types.ts     # Env / 端口接口
  test/          # vitest：DO 跑在假 state 上 + 可注入上游，全程不碰网络
```

## 契约

### 请求

```
POST {endpoint}/translate
Content-Type: application/json

{"clientId":"<8-64 位 [A-Za-z0-9-]，匿名安装 ID>","text":"...","from":"English 或 null","to":"Chinese"}
```

- `from` 为 null 表示自动检测。
- `to`/`from` 只接受**语言白名单**（约 35 种常用语言，见 `src/langs.ts`）：
  支持英文语言名（大小写不敏感）、语言代码（`zh`、`zh-CN`、`en`、`ja`…）
  与常见别名（`Simplified Chinese`、`Chinese (Simplified)`、`Mandarin`…），
  服务端统一规范化后进提示词。不在集合里 → 400 `UNSUPPORTED_LANGUAGE`。
  客户端设置里的自由文本译文语言若不在集合内会被拒（错误消息是人话中文）。
- 单请求上限 2000 字（按码点数计）；计费只按 `text`——`to`/`from` 经
  白名单后是封闭集合里的短规范名，无法夹带内容或放大提示词。

### 成功

```
200 {"translation":"整段译文"}
```

上游非流式、中转一次回完；客户端 `RelayBackend` 自己按句切分模拟流式。

### 健康检查

```
GET {endpoint}/health
200 {"status":"ok"}                    # 配置齐全
503 {"status":"error","problems":[...]} # 列出缺失的 secret/绑定（只报名字，不回显值）
```

### 错误（统一形状，message 一律人话中文）

| 状态 | code | 附加字段 | 含义 |
|---|---|---|---|
| 400 | `BAD_REQUEST` | | 请求体/字段不合法 |
| 400 | `TEXT_TOO_LONG` | `limit` | 超过单请求上限 |
| 400 | `UNSUPPORTED_LANGUAGE` | | `to`/`from` 不在语言白名单 |
| 429 | `RATE_LIMITED` | `retryAfterSeconds`（响应头另有 `Retry-After`） | 每 IP 突发限速 |
| 429 | `QUOTA_DEVICE` / `QUOTA_IP` / `QUOTA_GLOBAL` | `remaining`、`resetAt` | 对应层日配额用尽 |
| 502 | `UPSTREAM_ERROR` | | 上游失败（已退还本次计费） |
| 503 | `SERVICE_MISCONFIGURED` | | 缺盐/缺绑定，fail closed |
| 405 | `METHOD_NOT_ALLOWED` | | 非 POST |
| 404 | `NOT_FOUND` | | 路径不存在 |

配额错误的 `resetAt` 是下一个 UTC 零点（ISO 8601）；限速的
`retryAfterSeconds` 是当前分钟窗剩余秒数。

## 配额与限速

| 层 | 默认 | 说明 |
|---|---|---|
| 突发 | 60 次/分/IP | 固定分钟窗；IPv6 按 /64，`IP_PER_MINUTE_LIMIT` 可调 |
| 设备 | 20000 字/天 | 按 clientId（匿名安装 ID） |
| IP | 30000 字/天 | 按 `CF-Connecting-IP` 的加盐哈希，**IPv6 先折叠到 /64**（含 `::ffff:` 映射地址）；原始 IP 从不落存储 |
| 全局 | 200000 字/天 | 部署者的总闸，vars 里随流量调 |
| 单请求 | 2000 字 | 客户端也有同款前置检查 |

- 计数由**按日期分片的 Durable Object**（`DailyCounter`，SQLite 后端）
  原子完成：限速 + 三层"检查并预留"在同一次同步事务里，任一超限则全
  都不扣；无 KV 最终一致的竞态。UTC 00:00 换新分片即重置，旧分片由
  闹钟在两天后自清空。对象里只有计数，绝无正文或译文。
- 计费单位是源文本的码点数。
- **失败退款**：上游没给译文就把已计字符全额退还，用户不替通道的故障买单。
- 拒绝不记账：被拒的请求不占额度，也不占限速名额。

## Secrets 清单（绝不进仓库）

```bash
npx wrangler secret put ZHIPU_API_KEY   # 智谱开放平台的 key
npx wrangler secret put IP_HASH_SALT    # 随机长串；换盐=全体 IP 身份重置
```

**`IP_HASH_SALT` 是硬性要求（O-19 fail closed）**：缺失或空白时所有
翻译请求返回 503、`/health` 报 `IP_HASH_SALT 未设置`——绝不回退默认盐。

本地开发把同样的键放进 `server/.dev.vars`（已被 .gitignore 排除）。

生成盐的示例：`node -e "console.log(require('crypto').randomBytes(32).toString('hex'))"`

## 绑定与环境变量

- `QUOTA_COUNTER`：Durable Object 绑定（`DailyCounter`），随
  `wrangler.toml` 的 `[[migrations]] new_sqlite_classes` 在首次部署时
  自动创建；Workers 免费计划只允许 SQLite 后端的 DO，不需要也不再用 KV。
- vars（十进制字符串，均可省略用默认）：`DEVICE_DAILY_LIMIT`、
  `IP_DAILY_LIMIT`、`GLOBAL_DAILY_LIMIT`、`MAX_REQUEST_CHARS`、
  `IP_PER_MINUTE_LIMIT`、`UPSTREAM_MODEL`、`UPSTREAM_BASE_URL`。

## 部署步骤

前置：Node 18+、一个 Cloudflare 账号（Workers 免费档即可）。

```bash
cd server
npm install
npx wrangler login                       # 浏览器授权
npx wrangler secret put ZHIPU_API_KEY
npx wrangler secret put IP_HASH_SALT
npm test                                 # 本地门禁：内存计数器 + 假上游，78 例
npx wrangler deploy                      # DO migration 首次部署自动执行
curl -sS https://<你的域名>/health        # 期望 {"status":"ok"}
```

### 部署前检查清单

1. `npx wrangler secret list` 里有 `ZHIPU_API_KEY` 与 `IP_HASH_SALT`
   （盐缺失=全站 503，`/health` 会点名）。
2. `wrangler.toml` 的 `[[migrations]]` 与 `[[durable_objects.bindings]]`
   原样在（首次部署创建 `DailyCounter` 命名空间；不要删除 migration，
   存储类型一经创建不可更改）。
3. `/health` 返回 `{"status":"ok"}`。
4. **大陆网络对 `*.workers.dev` 整域不可达**：给 Worker **绑定自定义域名**
   （Cloudflare 上托管的域，Workers 路由或自定义域均可），否则国内用户
   连不上；客户端默认端点也要一并改（见下）。
5. 按流量预估复核 vars：全局日上限、`IP_PER_MINUTE_LIMIT`（批量翻译
   重的用户群可调高，默认 60 次/分/IP）。

部署后冒烟（把 URL 换成你的）：

```bash
curl -sS https://relay.example.com/translate \
  -H 'content-type: application/json' \
  -d '{"clientId":"smoke-test-0001","text":"Hello there.","from":null,"to":"Chinese"}'
# 期望 {"translation":"…"}；再发 2001 字文本期望 TEXT_TOO_LONG；
# to 给 "Klingon" 期望 UNSUPPORTED_LANGUAGE；
# 用掉设备额度后期望 429 且带 remaining/resetAt。
```

### 与客户端配对

客户端默认端点是 `RelayBackend.DefaultEndpoint`
（`src/Shiyu.Core/RelayBackend.cs`），**三处必须一起改**：该常量、
`AppSettings.RelayEndpoint` 的默认值、本文档。自建/测试时无需改代码：
启动拾语前设 `SHIYU_RELAY_URL=http://127.0.0.1:8787` 即指向
`npx wrangler dev` 起的本地 Worker（探针约定，与 `SHIYU_DATA_DIR` 同族，
只影响本次运行、不写设置文件）。

## 已知限制（明知的取舍）

- **clientId 可伪造**：它是配额身份不是鉴权（Glossy 作者同样承认）。
  伪造者绕过的只是每天 2 万字的份，IP 层（/64 聚合）、突发限速与全局
  封顶可兜底。
- **大陆网络对 `*.workers.dev` 整域不可达**：必须绑定自定义域名，
  默认 workers.dev 域名在国内无法访问。
- **无鉴权**：知道 URL 的人都能用，配额是唯一的闸。URL 泄露=额度被
  白嫖，必要时轮换 Worker 名或加一层共享口令（二期再说）。
- **单分片 DO 的吞吐上限**：同一 UTC 日所有请求串行过一枚
  `DailyCounter`（Cloudflare 给的单对象参考值约 500–1000 请求/秒），
  免费计划另有每日 DO 请求/行读写额度；当前规模（全局 20 万字/日）远在
  其下，若未来放大可再按小时细分片。
- **突发限速按固定分钟窗**：窗口交界处最多连过两窗的量（最多 2N-1 次）；
  默认 60 次/分对交互式使用与串行批量翻译足够，批量极重的场景调
  `IP_PER_MINUTE_LIMIT`。
- **上游单点**：只接智谱免费档；它挂了通道就挂（客户端有退避重试与
  人话报错）。多上游级联是二期候选。

## 本地开发

```bash
npm test          # vitest run（无需任何 secret；DO 跑在假 state 上）
npm run typecheck # tsc --noEmit
npm run dev       # wrangler dev（.dev.vars 里必须有两个 secret，缺盐会 503）
```
