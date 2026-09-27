import type { QuotaStore } from "../src/quota.js";

/**
 * 内存 KV：与 Cloudflare KV 同一最小接口（get/put+TTL），让配额逻辑与
 * 处理器在不碰网络、不碰 Miniflare 的情况下被测到。TTL 只记录不执行——
 * 过期清理由真实 KV 负责，这里关心的是键值形状。
 */
export class MemoryKV implements QuotaStore {
  private readonly map = new Map<string, string>();
  public readonly puts: Array<{ key: string; value: string; ttl: number }> = [];

  async get(key: string): Promise<string | null> {
    return this.map.get(key) ?? null;
  }

  async put(key: string, value: string, ttlSeconds: number): Promise<void> {
    this.puts.push({ key, value, ttl: ttlSeconds });
    this.map.set(key, value);
  }

  /** 测试观察口：某个键当下的计数值（无则 0）。 */
  count(key: string): number {
    return Number(this.map.get(key) ?? "0");
  }
}
