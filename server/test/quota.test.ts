import { describe, expect, it } from "vitest";
import type { Limits } from "../src/limits.js";
import { MemoryCounter, makeDailyCounter } from "./memory-counter.js";
import { FakeDoState } from "./fake-do-state.js";

const Limits: Limits = {
  deviceDaily: 200,
  ipDaily: 300,
  globalDaily: 400,
  maxRequestChars: 2000,
};

const Noon = new Date("2026-09-27T12:00:00Z");
const Tomorrow = new Date("2026-09-28T12:00:00Z");

function open() {
  const counter = new MemoryCounter(Limits);
  return { counter, state: counter.stateAt(Noon) };
}

/** 直接在假 state 上构造 DO 实例：生命周期测试（闹钟、自清空）用。 */
function openDo() {
  const state = new FakeDoState();
  return { daily: makeDailyCounter(state), state };
}

describe("三层配额：原子预留", () => {
  it("额度内放行并写入三层计数", async () => {
    const { state, counter } = open();

    const denial = await counter.admit({ clientId: "device-a", ipHash: "hash-1" }, 50, Noon);

    expect(denial).toBeNull();
    expect(state.count("d:device-a")).toBe(50);
    expect(state.count("i:hash-1")).toBe(50);
    expect(state.count("g")).toBe(50);
  });

  it("同一身份连续请求按字数累计", async () => {
    const { state, counter } = open();
    const identity = { clientId: "device-a", ipHash: "hash-1" };

    await counter.admit(identity, 50, Noon);
    await counter.admit(identity, 30, Noon);

    expect(state.count("d:device-a")).toBe(80);
  });

  it("同 IP 的不同设备共享 IP 层与全局层", async () => {
    const { state, counter } = open();

    await counter.admit({ clientId: "device-a", ipHash: "same-ip" }, 10, Noon);
    await counter.admit({ clientId: "device-b", ipHash: "same-ip" }, 20, Noon);

    expect(state.count("d:device-a")).toBe(10);
    expect(state.count("d:device-b")).toBe(20);
    expect(state.count("i:same-ip")).toBe(30);
    expect(state.count("g")).toBe(30);
  });

  it("并发请求顺序无关、精确累计——无丢失更新", async () => {
    const { state, counter } = open();
    const identity = { clientId: "device-a", ipHash: "hash-1" };

    await Promise.all(
      Array.from({ length: 10 }, () => counter.admit(identity, 10, Noon)),
    );

    expect(state.count("d:device-a")).toBe(100);
    expect(state.count("i:hash-1")).toBe(100);
    expect(state.count("g")).toBe(100);
  });
});

describe("三层配额：拒绝与全有或全无", () => {
  it("设备层耗尽时带剩余量与重置时间拒绝", async () => {
    const { counter } = open();
    const identity = { clientId: "device-a", ipHash: "hash-1" };

    await counter.admit(identity, 180, Noon);
    const denial = await counter.admit(identity, 50, Noon);

    // 剩 20 字、要 50 字：拒绝时告诉用户还剩多少、何时恢复。
    expect(denial).not.toBeNull();
    expect(denial!.code).toBe("QUOTA_DEVICE");
    expect(denial!.remaining).toBe(20);
    expect(denial!.resetAt).toBe("2026-09-28T00:00:00.000Z");
  });

  it("IP 层耗尽按 IP 层拒绝，哪怕该设备自己还有余量", async () => {
    const { counter } = open();

    await counter.admit({ clientId: "device-a", ipHash: "busy-ip" }, 150, Noon);
    await counter.admit({ clientId: "device-b", ipHash: "busy-ip" }, 140, Noon);
    const denial = await counter.admit({ clientId: "device-c", ipHash: "busy-ip" }, 20, Noon);

    expect(denial!.code).toBe("QUOTA_IP");
    expect(denial!.remaining).toBe(10);
  });

  it("IP 层超限时设备层与全局层都不扣——任一层超限则三层都不扣", async () => {
    const { state, counter } = open();

    // device-c 自己是 0，但 IP 层已被灌满：拒绝后 d/g 都不得有任何它的痕迹。
    await counter.admit({ clientId: "device-a", ipHash: "busy-ip" }, 150, Noon);
    await counter.admit({ clientId: "device-b", ipHash: "busy-ip" }, 150, Noon);
    const before = state.count("g");
    const denial = await counter.admit({ clientId: "device-c", ipHash: "busy-ip" }, 20, Noon);

    expect(denial!.code).toBe("QUOTA_IP");
    expect(state.count("d:device-c")).toBe(0);
    expect(state.count("g")).toBe(before);
  });

  it("全局层耗尽时任何身份都被拒", async () => {
    const { counter } = open();

    await counter.admit({ clientId: "device-a", ipHash: "ip-a" }, 150, Noon);
    await counter.admit({ clientId: "device-b", ipHash: "ip-b" }, 150, Noon);
    await counter.admit({ clientId: "device-c", ipHash: "ip-c" }, 100, Noon);
    const denial = await counter.admit({ clientId: "device-d", ipHash: "ip-d" }, 1, Noon);

    expect(denial!.code).toBe("QUOTA_GLOBAL");
    expect(denial!.remaining).toBe(0);
  });

  it("多层同时超限时报最先挡住的那一层", async () => {
    const { counter } = open();

    // 全局已被两台设备用满 400；同一台旧设备再要 1 个字时设备层与全局
    // 层同时越限——报先检查的设备层。
    await counter.admit({ clientId: "device-a", ipHash: "ip-a" }, 200, Noon);
    await counter.admit({ clientId: "device-b", ipHash: "ip-b" }, 200, Noon);
    const denial = await counter.admit({ clientId: "device-a", ipHash: "ip-a" }, 1, Noon);

    expect(denial!.code).toBe("QUOTA_DEVICE");
  });

  it("拒绝不写入任何计数——被拒的请求不占额度", async () => {
    const { state, counter } = open();

    await counter.admit({ clientId: "device-a", ipHash: "ip-a" }, 180, Noon);
    await counter.admit({ clientId: "device-a", ipHash: "ip-a" }, 50, Noon);

    expect(state.count("d:device-a")).toBe(180);
  });
});

describe("跨日重置", () => {
  it("UTC 零点换新分片：新的一天额度重新可用", async () => {
    const { counter } = open();
    const identity = { clientId: "device-a", ipHash: "ip-a" };

    await counter.admit(identity, 200, Noon);
    const denial = await counter.admit(identity, 1, Tomorrow);

    expect(denial).toBeNull();
    // 昨日的分片原封不动——重置不是清零，是换了账本。
    expect(counter.stateAt(Noon).count("d:device-a")).toBe(200);
    expect(counter.stateAt(Tomorrow).count("d:device-a")).toBe(1);
  });
});

describe("上游失败退款", () => {
  it("退款把三层计数都还回去", async () => {
    const { state, counter } = open();
    const identity = { clientId: "device-a", ipHash: "ip-a" };

    await counter.admit(identity, 100, Noon);
    await counter.refund(identity, 40, Noon);

    expect(state.count("d:device-a")).toBe(60);
    expect(state.count("i:ip-a")).toBe(60);
    expect(state.count("g")).toBe(60);
  });

  it("退款按预留同一天的桶退——跨日退款不动昨日的账", async () => {
    const { counter } = open();
    const identity = { clientId: "device-a", ipHash: "ip-a" };

    await counter.admit(identity, 100, Noon);
    await counter.refund(identity, 40, Tomorrow);

    expect(counter.stateAt(Noon).count("d:device-a")).toBe(100);
  });

  it("退款后的额度可以再次通过预留", async () => {
    const { counter } = open();
    const identity = { clientId: "device-a", ipHash: "ip-a" };

    await counter.admit(identity, 180, Noon);
    await counter.refund(identity, 100, Noon);
    const denial = await counter.admit(identity, 100, Noon);

    expect(denial).toBeNull();
  });

  it("退款不会把计数凿到负数", async () => {
    const { state, counter } = open();
    const identity = { clientId: "device-a", ipHash: "ip-a" };

    await counter.admit(identity, 50, Noon);
    await counter.refund(identity, 500, Noon);

    expect(state.count("d:device-a")).toBe(0);
  });
});

describe("日期分片对象的生命周期", () => {
  it("首个写入设一次闹钟：次日 +2 天自清空", async () => {
    const { daily, state } = openDo();

    await daily.admit({ clientId: "device-a", ipHash: "ip-a" }, 10, Limits, Noon);

    expect(state.alarmAt).toBe(Date.UTC(2026, 8, 29));
  });

  it("被拒绝的请求不设闹钟——只读不写不留善后", async () => {
    const { daily, state } = openDo();

    await daily.admit({ clientId: "device-a", ipHash: "ip-a" }, 250, Limits, Noon);

    expect(state.alarmAt).toBeNull();
  });

  it("闹钟触发后清空全部计数", async () => {
    const { daily, state } = openDo();
    await daily.admit({ clientId: "device-a", ipHash: "ip-a" }, 10, Limits, Noon);

    await daily.alarm();

    expect(state.count("d:device-a")).toBe(0);
    expect(state.count("g")).toBe(0);
  });
});
