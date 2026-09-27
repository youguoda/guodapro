import { buildPrompt } from "./prompt.js";

/** 一次上游翻译请求的形状。 */
export interface UpstreamRequest {
  text: string;
  from: string | null;
  to: string;
}

/** 上游端口：可注入，测试不碰网络。 */
export interface Upstream {
  translate(request: UpstreamRequest): Promise<string>;
}

/**
 * 上游失败的统一出口：状态码与上游细节都留在服务端，客户端只该看到
 * "暂时不可用"——重试与退款策略由中转统一掌握。
 */
export class UpstreamError extends Error {
  constructor(reason: string) {
    super(reason);
    this.name = "UpstreamError";
  }
}

export interface ZhipuOptions {
  apiKey: string;
  model: string;
  baseUrl: string;

  /** 可注入的 fetch：测试不碰网络。 */
  fetchImpl?: typeof fetch;

  /** 上游等待预算，默认 20s（Glossy 实证值）。 */
  timeoutMs?: number;
}

/**
 * 智谱开放平台的 OpenAI 兼容调用：glm-4-flash 免费档，非流式。
 * 非流式是刻意的选择——中转把整段答案拿稳了再一次性回给客户端，
 * 客户端自己负责把整段答案化妆成流式（RelayBackend 按句切分）。
 */
export class ZhipuUpstream implements Upstream {
  private readonly fetchImpl: typeof fetch;
  private readonly timeoutMs: number;

  constructor(private readonly options: ZhipuOptions) {
    this.fetchImpl = options.fetchImpl ?? fetch;
    this.timeoutMs = options.timeoutMs ?? 20000;
  }

  async translate(request: UpstreamRequest): Promise<string> {
    const { system, user } = buildPrompt(request);
    const body = {
      model: this.options.model,
      stream: false,
      temperature: 0.2,
      messages: [
        { role: "system", content: system },
        { role: "user", content: user },
      ],
    };

    let response: Response;
    try {
      response = await this.fetchImpl(`${this.options.baseUrl.replace(/\/$/, "")}/chat/completions`, {
        method: "POST",
        headers: {
          "content-type": "application/json",
          authorization: `Bearer ${this.options.apiKey}`,
        },
        body: JSON.stringify(body),
        signal: AbortSignal.timeout(this.timeoutMs),
      });
    } catch (error) {
      // 含超时与网络错误：对客户端都是同一件事——上游没应答。
      throw new UpstreamError(`upstream unreachable: ${(error as Error).message}`);
    }

    if (!response.ok) {
      throw new UpstreamError(`upstream status ${response.status}`);
    }

    let payload: unknown;
    try {
      payload = await response.json();
    } catch (error) {
      throw new UpstreamError(`upstream sent unparseable json: ${(error as Error).message}`);
    }

    const content = (payload as { choices?: Array<{ message?: { content?: string } }> })
      ?.choices?.[0]?.message?.content;
    if (typeof content !== "string") {
      throw new UpstreamError("upstream reply carried no content");
    }

    return content;
  }
}
