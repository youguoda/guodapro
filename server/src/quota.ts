import type { DurableObjectNamespace } from "@cloudflare/workers-types";
import { DurableObject } from "cloudflare:workers";
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
 * 原子配额计数端口（O-19）：admit 在一次调用里对设备/IP/全局三层做
 * "检查并预留"——全部满足才扣减，任一层超限则三层都不扣。处理器只依赖
 * 这个接口：真实部署走 DurableCounter（Durable Object RPC），单测走
 * 同一个 DailyCounter 类 + 假 state 的内存实现。
 */
export interface QuotaCounter {
  admit(identity: Identity, chars: number, now: Date): Promise<QuotaDenial | null>;
  refund(identity: Identity, chars: number, now: Date): Promise<void>;
}

const Messages: Record<QuotaDenial["code"], string> = {
  QUOTA_DEVICE: "今日设备免费额度已用完。",
  QUOTA_IP: "当前网络今日的免费额度已用完。",
  QUOTA_GLOBAL: "公共通道今日额度已用完，明天再来。",
};

/**
 * 按日期分片的计数器 Durable Object（O-19）：一天一个对象
 * （`getByName(<UTC 日期>)`），三层检查与扣减在同一次同步事务里完成。
 * Durable Object 的事件模型保证同一对象上的调用串行、无 await 的读写
 * 合并为一个原子事务——KV 读-改-写的竞态面就此消失。存储里只有计数，
 * 绝无正文或译文。
 */
export class DailyCounter extends DurableObject {
  /** 三层"检查并预留"；任一层超限则什么都不写。 */
  async admit(
    identity: Identity,
    chars: number,
    limits: Limits,
    now: Date,
  ): Promise<QuotaDenial | null> {
    const kv = this.ctx.storage.kv;
    const denial = this.ctx.storage.transactionSync((): QuotaDenial | null => {
      const keys = [`d:${identity.clientId}`, `i:${identity.ipHash}`, "g"];
      const used = keys.map((key) => kv.get<number>(key) ?? 0);

      const layers: Array<{ layer: QuotaDenial["code"]; limit: number; index: number }> = [
        { layer: "QUOTA_DEVICE", limit: limits.deviceDaily, index: 0 },
        { layer: "QUOTA_IP", limit: limits.ipDaily, index: 1 },
        { layer: "QUOTA_GLOBAL", limit: limits.globalDaily, index: 2 },
      ];

      for (const { layer, limit, index } of layers) {
        if (used[index] + chars > limit) {
          return {
            code: layer,
            message: Messages[layer],
            remaining: Math.max(0, limit - used[index]),
            resetAt: resetAt(now),
          };
        }
      }

      for (const [index, key] of keys.entries()) {
        kv.put(key, used[index] + chars);
      }

      return null;
    });

    if (denial === null) {
      await this.ensureSweeper(now);
    }
    return denial;
  }

  /** 上游失败时退还已计字符；退到零为止，不产生负数。 */
  async refund(identity: Identity, chars: number, now: Date): Promise<void> {
    const kv = this.ctx.storage.kv;
    this.ctx.storage.transactionSync(() => {
      const keys = [`d:${identity.clientId}`, `i:${identity.ipHash}`, "g"];
      const used = keys.map((key) => kv.get<number>(key) ?? 0);
      for (const [index, key] of keys.entries()) {
        kv.put(key, Math.max(0, used[index] - chars));
      }
    });
    await this.ensureSweeper(now);
  }

  /** 跨日分片对象的善后：首个写入口设一次闹钟，次日 +2 天自清空。 */
  private async ensureSweeper(now: Date): Promise<void> {
    if ((await this.ctx.storage.getAlarm()) === null) {
      const sweepAt = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + 2);
      await this.ctx.storage.setAlarm(sweepAt);
    }
  }

  /** 日期分片过期后永远不会再被引用，留下的只有计数垃圾——清掉。 */
  async alarm(): Promise<void> {
    await this.ctx.storage.deleteAll();
  }
}

/** 下一个 UTC 零点——所有层的重置时刻。 */
function resetAt(now: Date): string {
  const next = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + 1);
  return new Date(next).toISOString();
}

/** 对象名即分片键：UTC 日期。 */
export function dayKey(now: Date): string {
  return now.toISOString().slice(0, 10);
}

/**
 * 真实部署的适配器：把端口调用转发到当日分片的 Durable Object。
 * 限额在 Worker 侧读取（vars 可热调），随每次调用下发——DO 保持无配置。
 */
export class DurableCounter implements QuotaCounter {
  constructor(
    private readonly namespace: DurableObjectNamespace<DailyCounter>,
    private readonly limits: Limits,
  ) {}

  admit(identity: Identity, chars: number, now: Date): Promise<QuotaDenial | null> {
    return this.namespace.getByName(dayKey(now)).admit(identity, chars, this.limits, now);
  }

  refund(identity: Identity, chars: number, now: Date): Promise<void> {
    return this.namespace.getByName(dayKey(now)).refund(identity, chars, now);
  }
}
