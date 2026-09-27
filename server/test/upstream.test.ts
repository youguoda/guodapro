import { describe, expect, it } from "vitest";
import { buildPrompt } from "../src/prompt.js";
import { UpstreamError, ZhipuUpstream } from "../src/upstream.js";

describe("翻译 prompt", () => {
  it("带着目标语言与原文，并压制加戏", () => {
    const { system, user } = buildPrompt({ text: "Hello there", from: null, to: "Chinese" });

    expect(system).toContain("translation");
    expect(user).toContain("Hello there");
    expect(user).toContain("Chinese");
  });

  it("未声明源语言时让模型自己判断", () => {
    const { user } = buildPrompt({ text: "Bonjour", from: null, to: "Chinese" });

    expect(user).toContain("the language it is written in");
  });

  it("声明的源语言进入指令", () => {
    const { user } = buildPrompt({ text: "Bonjour", from: "French", to: "Chinese" });

    expect(user).toContain("French");
  });
});

function okResponse(content: string) {
  return new Response(
    JSON.stringify({
      choices: [{ message: { role: "assistant", content } }],
    }),
    { status: 200, headers: { "content-type": "application/json" } },
  );
}

/** 记录真实发出的请求（含 init 合成的 method/头），再交给应答桩。 */
function makeUpstream(respond: () => Promise<Response>) {
  const seen: Request[] = [];
  const upstream = new ZhipuUpstream({
    apiKey: "an-upstream-key",
    model: "glm-4-flash",
    baseUrl: "https://upstream.example/api/paas/v4",
    fetchImpl: async (input, init) => {
      seen.push(input instanceof Request ? input : new Request(input, init));
      return respond();
    },
  });
  return { upstream, seen };
}

describe("智谱上游", () => {
  it("按 OpenAI 兼容形状非流式调用并取回整段译文", async () => {
    const { upstream, seen } = makeUpstream(async () => okResponse("你好。"));

    const translation = await upstream.translate({ text: "Hello.", from: null, to: "Chinese" });

    expect(translation).toBe("你好。");
    const request = seen[0];
    expect(request.url).toBe("https://upstream.example/api/paas/v4/chat/completions");
    expect(request.method).toBe("POST");
    expect(request.headers.get("authorization")).toBe("Bearer an-upstream-key");

    const body = (await request.json()) as {
      model: string;
      stream: boolean;
      temperature: number;
      messages: Array<{ role: string; content: string }>;
    };
    expect(body.model).toBe("glm-4-flash");
    expect(body.stream).toBe(false);
    expect(body.temperature).toBe(0.2);
    expect(body.messages[0].role).toBe("system");
    expect(body.messages[1].content).toContain("Hello.");
    expect(body.messages[1].content).toContain("Chinese");
  });

  it("上游拒绝时抛上游错误而不是把状态透传给客户端", async () => {
    const { upstream } = makeUpstream(async () => new Response("nope", { status: 429 }));

    await expect(upstream.translate({ text: "hi", from: null, to: "Chinese" }))
      .rejects.toBeInstanceOf(UpstreamError);
  });

  it("上游应答缺了 choices 时同样按上游错误处理", async () => {
    const { upstream } = makeUpstream(async () =>
      new Response(JSON.stringify({ error: { code: "1113", message: "余额不足" } }), {
        status: 200,
        headers: { "content-type": "application/json" },
      }),
    );

    await expect(upstream.translate({ text: "hi", from: null, to: "Chinese" }))
      .rejects.toBeInstanceOf(UpstreamError);
  });
});
