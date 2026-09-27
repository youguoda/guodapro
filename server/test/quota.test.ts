import { describe, expect, it } from "vitest";
import { Quota } from "../src/quota.js";
import type { Limits } from "../src/limits.js";
import { MemoryKV } from "./memory-kv.js";

const Limits: Limits = {
  deviceDaily: 200,
  ipDaily: 300,
  globalDaily: 400,
  maxRequestChars: 2000,
};

const Noon = new Date("2026-09-27T12:00:00Z");
const Tomorrow = new Date("2026-09-28T12:00:00Z");

function open(now: Date = Noon) {
  const kv = new MemoryKV();
  const quota = new Quota(kv, Limits, () => now);
  return { kv, quota };
}

describe("四层配额：预留", () => {
  it("额度内放行并写入三层计数", async () => {
    const { kv, quota } = open();

    const denial = await quota.reserve({ clientId: "device-a", ipHash: "hash-1" }, 50);

    expect(denial).toBeNull();
    expect(kv.count("d:device-a:2026-09-27")).toBe(50);
    expect(kv.count("i:hash-1:2026-09-27")).toBe(50);
    expect(kv.count("g:2026-09-27")).toBe(50);
  });

  it("同一身份连续请求按字数累计", async () => {
    const { kv, quota } = open();
    const identity = { clientId: "device-a", ipHash: "hash-1" };

    await quota.reserve(identity, 50);
    await quota.reserve(identity, 30);

    expect(kv.count("d:device-a:2026-09-27")).toBe(80);
  });

  it("同 IP 的不同设备共享 IP 层与全局层", async () => {
    const { kv, quota } = open();

    await quota.reserve({ clientId: "device-a", ipHash: "same-ip" }, 10);
    await quota.reserve({ clientId: "device-b", ipHash: "same-ip" }, 20);

    expect(kv.count("d:device-a:2026-09-27")).toBe(10);
    expect(kv.count("d:device-b:2026-09-27")).toBe(20);
    expect(kv.count("i:same-ip:2026-09-27")).toBe(30);
    expect(kv.count("g:2026-09-27")).toBe(30);
  });

  it("计数写入带 TTL，隔天桶由 KV 自己过期", async () => {
    const { kv, quota } = open();

    await quota.reserve({ clientId: "device-a", ipHash: "hash-1" }, 10);

    expect(kv.puts.length).toBe(3);
    for (const put of kv.puts) {
      expect(put.ttl).toBeGreaterThan(0);
    }
  });
});

describe("四层配额：拒绝", () => {
  it("设备层耗尽时带剩余量与重置时间拒绝", async () => {
    const { quota } = open();

    await quota.reserve({ clientId: "device-a", ipHash: "hash-1" }, 180);
    const denial = await quota.reserve({ clientId: "device-a", ipHash: "hash-1" }, 50);

    // 剩 20 字、要 50 字：拒绝时告诉用户还剩多少、何时恢复。
    expect(denial).not.toBeNull();
    expect(denial!.code).toBe("QUOTA_DEVICE");
    expect(denial!.remaining).toBe(20);
    expect(denial!.resetAt).toBe("2026-09-28T00:00:00.000Z");
  });

  it("IP 层耗尽按 IP 层拒绝，哪怕该设备自己还有余量", async () => {
    const { quota } = open();

    // 两台设备各用一点，IP 层先到 290；第三台再来 20 就越过 300。
    await quota.reserve({ clientId: "device-a", ipHash: "busy-ip" }, 150);
    await quota.reserve({ clientId: "device-b", ipHash: "busy-ip" }, 140);
    const denial = await quota.reserve({ clientId: "device-c", ipHash: "busy-ip" }, 20);

    expect(denial!.code).toBe("QUOTA_IP");
    expect(denial!.remaining).toBe(10);
  });

  it("全局层耗尽时任何身份都被拒", async () => {
    const { quota } = open();

    // 设备层 200、IP 层 300 都限制不了下面的组合，全局层先到 400。
    await quota.reserve({ clientId: "device-a", ipHash: "ip-a" }, 150);
    await quota.reserve({ clientId: "device-b", ipHash: "ip-b" }, 150);
    await quota.reserve({ clientId: "device-c", ipHash: "ip-c" }, 100);
    const denial = await quota.reserve({ clientId: "device-d", ipHash: "ip-d" }, 1);

    expect(denial!.code).toBe("QUOTA_GLOBAL");
    expect(denial!.remaining).toBe(0);
  });

  it("多层同时超限时报最先挡住的那一层", async () => {
    const { quota } = open();

    // 全局已被两台设备用满 400；同一台旧设备再要 1 个字时设备层与全局
    // 层同时越限——报先检查的设备层。
    await quota.reserve({ clientId: "device-a", ipHash: "ip-a" }, 200);
    await quota.reserve({ clientId: "device-b", ipHash: "ip-b" }, 200);
    const denial = await quota.reserve({ clientId: "device-a", ipHash: "ip-a" }, 1);

    expect(denial!.code).toBe("QUOTA_DEVICE");
  });

  it("拒绝不写入任何计数——被拒的请求不占额度", async () => {
    const { kv, quota } = open();

    await quota.reserve({ clientId: "device-a", ipHash: "ip-a" }, 180);
    await quota.reserve({ clientId: "device-a", ipHash: "ip-a" }, 50);

    expect(kv.count("d:device-a:2026-09-27")).toBe(180);
  });

  it("UTC 零点重置：新的一天是新的桶", async () => {
    const { quota } = open();
    await quota.reserve({ clientId: "device-a", ipHash: "ip-a" }, 200);

    const nextDay = new Quota(new MemoryKV(), Limits, () => Tomorrow);
    const denial = await nextDay.reserve({ clientId: "device-a", ipHash: "ip-a" }, 1);

    expect(denial).toBeNull();
  });
});

describe("上游失败退款", () => {
  it("退款把三层计数都还回去", async () => {
    const { kv, quota } = open();
    const identity = { clientId: "device-a", ipHash: "ip-a" };

    await quota.reserve(identity, 100);
    await quota.refund(identity, 40);

    expect(kv.count("d:device-a:2026-09-27")).toBe(60);
    expect(kv.count("i:ip-a:2026-09-27")).toBe(60);
    expect(kv.count("g:2026-09-27")).toBe(60);
  });

  it("退款后的额度可以再次通过预留", async () => {
    const { quota } = open();
    const identity = { clientId: "device-a", ipHash: "ip-a" };

    await quota.reserve(identity, 180);
    await quota.refund(identity, 100);
    const denial = await quota.reserve(identity, 100);

    expect(denial).toBeNull();
  });

  it("退款不会把计数凿到负数", async () => {
    const { kv, quota } = open();
    const identity = { clientId: "device-a", ipHash: "ip-a" };

    await quota.reserve(identity, 50);
    await quota.refund(identity, 500);

    expect(kv.count("d:device-a:2026-09-27")).toBe(0);
  });
});
