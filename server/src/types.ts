import type { DurableObjectNamespace } from "@cloudflare/workers-types";
import type { DailyCounter, Identity, QuotaCounter, QuotaDenial } from "./quota.js";
import type { Limits } from "./limits.js";
import type { Upstream } from "./upstream.js";

export type { DailyCounter, Identity, QuotaCounter, QuotaDenial, Limits, Upstream };

/**
 * Worker 绑定的环境：密钥走 secret（绝不进仓库），限额走 vars，
 * 计数走按日期分片的 Durable Object。客户端零密钥——凭据只活在这里。
 */
export interface Env {
  /** 智谱开放平台的 API key。只能经 `wrangler secret put` 写入。 */
  ZHIPU_API_KEY: string;

  /** IP 加盐哈希的盐。同上，secret；缺失或空白时全部请求 503（fail closed）。 */
  IP_HASH_SALT: string;

  /** 每设备每日字符数，十进制字符串，默认 20000。 */
  DEVICE_DAILY_LIMIT?: string;

  /** 每 IP（IPv6 按 /64）每日字符数，十进制字符串，默认 30000。 */
  IP_DAILY_LIMIT?: string;

  /** 全局每日字符数封顶，十进制字符串，默认 200000（部署者按流量调）。 */
  GLOBAL_DAILY_LIMIT?: string;

  /** 单请求字符上限，十进制字符串，默认 2000。 */
  MAX_REQUEST_CHARS?: string;

  /** 上游模型名，默认 glm-4-flash（免费档）。 */
  UPSTREAM_MODEL?: string;

  /** 上游 OpenAI 兼容根地址。 */
  UPSTREAM_BASE_URL?: string;

  /** 原子配额计数：按日期分片的 Durable Object（SQLite 后端，O-19）。 */
  QUOTA_COUNTER: DurableObjectNamespace<DailyCounter>;
}

/** 处理器的全部依赖；省略时从 Env 现场构建（真实部署路径）。 */
export interface RelayServices {
  quota: QuotaCounter;
  upstream: Upstream;
  limits: Limits;
  now: () => Date;
}
