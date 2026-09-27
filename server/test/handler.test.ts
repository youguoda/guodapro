import { describe, expect, it } from "vitest";
import type { Env, RelayServices, Upstream } from "../src/types.js";
import { Quota, type QuotaStore } from "../src/quota.js";
import type { Limits } from "../src/limits.js";
import { handleTranslate } from "../src/handler.js";
import worker from "../src/index.js";
import { saltedHash } from "../src/hash.js";
import { MemoryKV } from "./memory-kv.js";

const Limits: Limits = {
  deviceDaily: 200,
  ipDaily: 300,
  globalDaily: 400,
  maxRequestChars: 50,
};

const Noon = new Date("2026-09-27T12:00:00Z");
const Day = "2026-09-27";

// 测试 IP 的真实加盐哈希——处理器的记账键里只能有它，不能有原始 IP。
const IpHash = await saltedHash("test-salt", "203.0.113.7");

function fakeUpstream(translation = "你好。") {
  const calls: Array<{ text: string; from: string | null; to: string }> = [];
  const upstream: Upstream = {
    async translate(request) {
      calls.push(request);
      return translation;
    },
  };
  return { upstream, calls };
}

const failingUpstream: Upstream = {
  async translate() {
    throw new Error("upstream exploded with internals");
  },
};

function open(upstream: Upstream, kv: QuotaStore = new MemoryKV()) {
  const env = { IP_HASH_SALT: "test-salt" } as unknown as Env;
  const services: RelayServices = {
    quota: new Quota(kv, Limits, () => Noon),
    upstream,
    limits: Limits,
    now: () => Noon,
  };
  return { env, services, kv };
}

async function call(
  env: Env,
  services: RelayServices,
  body: unknown,
  options: { method?: string; ip?: string } = {},
) {
  const method = options.method ?? "POST";
  const request = new Request("https://relay.example.com/translate", {
    method,
    headers: {
      "content-type": "application/json",
      "CF-Connecting-IP": options.ip ?? "203.0.113.7",
    },
    // GET/HEAD 不允许带体——405 路径本来也不需要体。
    ...(method === "GET" ? {} : { body: typeof body === "string" ? body : JSON.stringify(body) }),
  });
  return handleTranslate(request, env, services);
}

function body(clientId = "client-1234", text = "hello", to = "Chinese", from: string | null = null) {
  return { clientId, text, from, to };
}

/** workers-types 里 json() 返回 unknown；测试统一从这里取类型化错误体。 */
async function errorOf(response: Response): Promise<{
  code: string;
  message: string;
  remaining?: number;
  limit?: number;
  resetAt?: string;
}> {
  const payload = (await response.json()) as {
    error: { code: string; message: string; remaining?: number; limit?: number; resetAt?: string };
  };
  return payload.error;
}

describe("/translate：成功路径", () => {
  it("返回整段译文，并按字数给三层记账", async () => {
    const { upstream, calls } = fakeUpstream("你好。世界。");
    const kv = new MemoryKV();
    const { env, services } = open(upstream, kv);

    const response = await call(env, services, body(undefined, "hello world"));

    expect(response.status).toBe(200);
    const payload = (await response.json()) as { translation: string };
    expect(payload.translation).toBe("你好。世界。");

    // 计费单位是码点数：11 个字符的原文就记 11，且 IP 层只存加盐哈希。
    expect(kv.count(`d:client-1234:${Day}`)).toBe(11);
    expect(kv.count(`i:${IpHash}:${Day}`)).toBe(11);
    expect(kv.count(`g:${Day}`)).toBe(11);

    expect(calls).toEqual([{ text: "hello world", from: null, to: "Chinese" }]);
  });

  it("声明的源语言原样传给上游", async () => {
    const { upstream, calls } = fakeUpstream();
    const { env, services } = open(upstream);

    await call(env, services, body(undefined, "hello", "Chinese", "English"));

    expect(calls[0].from).toBe("English");
  });
});

describe("/translate：请求校验", () => {
  it("不是 JSON 的请求体被 400 拒绝", async () => {
    const { env, services } = open(fakeUpstream().upstream);

    const response = await call(env, services, "this is not json");

    expect(response.status).toBe(400);
    expect((await errorOf(response)).code).toBe("BAD_REQUEST");
  });

  it("clientId 格式不对被 400 拒绝", async () => {
    const { env, services } = open(fakeUpstream().upstream);

    const response = await call(env, services, body("x"));

    expect(response.status).toBe(400);
    expect((await errorOf(response)).code).toBe("BAD_REQUEST");
  });

  it("空文本被 400 拒绝", async () => {
    const { env, services } = open(fakeUpstream().upstream);

    const response = await call(env, services, body(undefined, ""));

    expect(response.status).toBe(400);
  });

  it("缺少目标语言被 400 拒绝", async () => {
    const { env, services } = open(fakeUpstream().upstream);

    const response = await call(env, services, { clientId: "client-1234", text: "hi", from: null });

    expect(response.status).toBe(400);
  });

  it("超长文本被 400 拒绝，错误里带着上限", async () => {
    const { env, services } = open(fakeUpstream().upstream);

    const response = await call(env, services, body(undefined, "a".repeat(51)));

    expect(response.status).toBe(400);
    const error = await errorOf(response);
    expect(error.code).toBe("TEXT_TOO_LONG");
    expect(error.limit).toBe(50);
  });

  it("GET 一律 405，不进配额也不进上游", async () => {
    const { upstream, calls } = fakeUpstream();
    const { env, services } = open(upstream);

    const response = await call(env, services, body(), { method: "GET" });

    expect(response.status).toBe(405);
    expect(calls).toEqual([]);
  });
});

describe("/translate：失败退款", () => {
  it("上游失败时退还已计字符并只给人话", async () => {
    const kv = new MemoryKV();
    const { env, services } = open(failingUpstream, kv);

    const response = await call(env, services, body(undefined, "hello"));

    expect(response.status).toBe(502);
    const error = await errorOf(response);
    expect(error.code).toBe("UPSTREAM_ERROR");
    expect(error.message).not.toContain("exploded");

    // 退到零：下一个请求不该替失败的那次买单。
    expect(kv.count(`d:client-1234:${Day}`)).toBe(0);
    expect(kv.count(`g:${Day}`)).toBe(0);
  });

  it("退款后的额度立刻可用", async () => {
    const kv = new MemoryKV();
    const fail = open(failingUpstream, kv);
    await call(fail.env, fail.services, body());

    const ok = open(fakeUpstream().upstream, kv);
    const response = await call(ok.env, ok.services, body());

    expect(response.status).toBe(200);
  });
});

describe("/translate：配额拒绝", () => {
  it("设备层耗尽时带剩余量与重置时间拒绝", async () => {
    const kv = new MemoryKV();
    const { env, services } = open(fakeUpstream().upstream, kv);

    // 该设备今天已用 196 字；再来 5 字的请求越线，剩 4 字如实告知。
    await services.quota.reserve({ clientId: "client-1234", ipHash: IpHash }, 196);
    const response = await call(env, services, body("client-1234", "hello"));

    expect(response.status).toBe(429);
    const error = await errorOf(response);
    expect(error.code).toBe("QUOTA_DEVICE");
    expect(error.remaining).toBe(4);
    expect(error.resetAt).toBe("2026-09-28T00:00:00.000Z");
    expect(error.message.length).toBeGreaterThan(0);
  });

  it("同 IP 的第二台设备按 IP 层拒绝", async () => {
    const kv = new MemoryKV();
    const { env, services } = open(fakeUpstream().upstream, kv);

    // 同 IP 两台设备先用掉 298 字（各不越自己的 200）；第三台再要 5 字
    // 就越过 IP 层的 300。
    await services.quota.reserve({ clientId: "device-a", ipHash: IpHash }, 150);
    await services.quota.reserve({ clientId: "device-b", ipHash: IpHash }, 148);
    const response = await call(env, services, body("device-c", "hello"), { ip: "203.0.113.7" });

    expect(response.status).toBe(429);
    expect((await errorOf(response)).code).toBe("QUOTA_IP");
  });

  it("配额拒绝不惊动上游", async () => {
    const kv = new MemoryKV();
    const { upstream, calls } = fakeUpstream();
    const { env, services } = open(upstream, kv);

    await services.quota.reserve({ clientId: "client-1234", ipHash: IpHash }, 200);
    const response = await call(env, services, body("client-1234", "hello"));

    expect(response.status).toBe(429);
    expect(calls).toEqual([]);
  });
});

describe("路由", () => {
  it("不存在的路径 404", async () => {
    const response = await worker.fetch(
      new Request("https://relay.example.com/other", { method: "POST" }),
      {} as Env,
    );

    expect(response.status).toBe(404);
    expect((await errorOf(response)).code).toBe("NOT_FOUND");
  });
});
