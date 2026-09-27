import type { KVNamespace } from "@cloudflare/workers-types";
import type { Limits } from "./limits.js";

/** 配额身份：设备匿名 ID + IP 加盐哈希。 */
export interface Identity {
  clientId: string;
  ipHash: string;
}

/** 一层配额被用尽时的事实：错误码、剩余量与重置时间一起走。 */
export interface QuotaDenial {
  code: "QUOTA_DEVICE" | "QUOTA_IP" | "QUOTA_GLOBAL";
  message: string;
  remaining: number;
  resetAt: string;
}

/**
 * 计数存储的最小接口：真实 KV 与测试内存实现都长这样。
 * KV 的最终一致性意味着并发下计数可能偏松——这是明知的取舍
 * （见 README"已知限制"），免费通道要的是便宜与简单，不是精确到字。
 */
export interface QuotaStore {
  get(key: string): Promise<string | null>;
  put(key: string, value: string, ttlSeconds: number): Promise<void>;
}

/** 真实 KV 的适配器。 */
export class KvStore implements QuotaStore {
  constructor(private readonly kv: KVNamespace) {}

  get(key: string): Promise<string | null> {
    return this.kv.get(key);
  }

  put(key: string, value: string, ttlSeconds: number): Promise<void> {
    return this.kv.put(key, value, { expirationTtl: ttlSeconds });
  }
}

const Messages: Record<QuotaDenial["code"], string> = {
  QUOTA_DEVICE: "今日设备免费额度已用完。",
  QUOTA_IP: "当前网络今日的免费额度已用完。",
  QUOTA_GLOBAL: "公共通道今日额度已用完，明天再来。",
};

/**
 * 三层日配额：设备 2 万、IP 3 万、全局封顶，00:00 UTC 重置。
 * 桶键带 UTC 日期，过期由 KV 的 TTL 收尾；预留失败不记账，上游失败
 * 由调用方按已计字符退款——用户不替我们的上游故障买单。
 */
export class Quota {
  constructor(
    private readonly store: QuotaStore,
    private readonly limits: Limits,
    private readonly now: () => Date,
  ) {}

  /** 记下 chars 字的消耗；放行返回 null，拒绝返回该层的事实。 */
  async reserve(identity: Identity, chars: number): Promise<QuotaDenial | null> {
    const keys = this.keys(identity);
    const used = await this.readAll(keys);

    const layers: Array<{ layer: QuotaDenial["code"]; limit: number; index: number }> = [
      { layer: "QUOTA_DEVICE", limit: this.limits.deviceDaily, index: 0 },
      { layer: "QUOTA_IP", limit: this.limits.ipDaily, index: 1 },
      { layer: "QUOTA_GLOBAL", limit: this.limits.globalDaily, index: 2 },
    ];

    for (const { layer, limit, index } of layers) {
      if (used[index] + chars > limit) {
        return {
          code: layer,
          message: Messages[layer],
          remaining: Math.max(0, limit - used[index]),
          resetAt: this.resetAt(),
        };
      }
    }

    for (const [index, key] of keys.entries()) {
      await this.store.put(key, String(used[index] + chars), BucketTtlSeconds);
    }

    return null;
  }

  /** 上游失败时退还已计字符；退到零为止，不产生负数。 */
  async refund(identity: Identity, chars: number): Promise<void> {
    const keys = this.keys(identity);
    const used = await this.readAll(keys);

    for (const [index, key] of keys.entries()) {
      await this.store.put(key, String(Math.max(0, used[index] - chars)), BucketTtlSeconds);
    }
  }

  /** 三个桶键：d=设备、i=IP 哈希、g=全局，后缀都是 UTC 日期。 */
  private keys(identity: Identity): [string, string, string] {
    const day = dayKey(this.now());
    return [
      `d:${identity.clientId}:${day}`,
      `i:${identity.ipHash}:${day}`,
      `g:${day}`,
    ];
  }

  private async readAll(keys: readonly string[]): Promise<[number, number, number]> {
    const values = await Promise.all(keys.map((key) => this.store.get(key)));
    return values.map((value) => Math.max(0, Number.parseInt(value ?? "0", 10) || 0)) as [
      number,
      number,
      number,
    ];
  }

  /** 下一个 UTC 零点——所有桶的重置时刻。 */
  private resetAt(): string {
    const now = this.now();
    const next = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + 1);
    return new Date(next).toISOString();
  }
}

/** 桶活两天：隔天即无用，让 KV 自己回收。 */
const BucketTtlSeconds = 2 * 24 * 60 * 60;

function dayKey(now: Date): string {
  return now.toISOString().slice(0, 10);
}
