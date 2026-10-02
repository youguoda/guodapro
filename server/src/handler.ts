import type { Env, RelayServices } from "./types.js";
import { readLimits } from "./limits.js";
import { saltedHash } from "./hash.js";
import { normalizeLanguage } from "./langs.js";
import { KvStore, Quota, type Identity } from "./quota.js";
import { ZhipuUpstream } from "./upstream.js";

const ClientIdPattern = /^[A-Za-z0-9-]{8,64}$/;

/**
 * POST /translate 的全部编排：校验 → 四层配额预留 → 上游 → 失败退款。
 * 依赖全部可注入——测试用内存 KV 与假上游走完同一条路，不需要
 * Miniflare，也不需要网络。
 */
export async function handleTranslate(
  request: Request,
  env: Env,
  injected: Partial<RelayServices> = {},
): Promise<Response> {
  if (request.method !== "POST") {
    return jsonError(405, "METHOD_NOT_ALLOWED", "只接受 POST。");
  }

  const limits = injected.limits ?? readLimits(env);
  const now = injected.now ?? (() => new Date());
  const quota = injected.quota ?? new Quota(new KvStore(env.QUOTA), limits, now);
  const upstream = injected.upstream
    ?? new ZhipuUpstream({
      apiKey: env.ZHIPU_API_KEY,
      model: env.UPSTREAM_MODEL ?? "glm-4-flash",
      baseUrl: env.UPSTREAM_BASE_URL ?? "https://open.bigmodel.cn/api/paas/v4",
    });

  let raw: unknown;
  try {
    raw = await request.json();
  } catch {
    return jsonError(400, "BAD_REQUEST", "请求体不是合法 JSON。");
  }

  const parsed = parseBody(raw);
  if ("message" in parsed) {
    return jsonError(400, "BAD_REQUEST", parsed.message);
  }

  const { clientId, text, from: rawFrom, to: rawTo } = parsed.value;

  // 语言白名单：to/from 只认封闭集合（含语言代码与常见别名），折叠成
  // 规范名后才进提示词——自由文本的 to/from 是提示词注入与额度绕过的
  // 入口（O-19）。
  const to = normalizeLanguage(rawTo);
  if (!to) {
    return jsonError(400, "UNSUPPORTED_LANGUAGE", "暂不支持这种目标语言（to）。");
  }
  const from = rawFrom === null ? null : normalizeLanguage(rawFrom);
  if (rawFrom !== null && from === null) {
    return jsonError(400, "UNSUPPORTED_LANGUAGE", "暂不支持这种源语言（from），留空可自动识别。");
  }

  // 计费与上限都按码点数：一个汉字与一个字母同价。只计 text 是安全的：
  // to/from 经白名单后是从固定词表里选出的规范名（集合封闭、长度有界），
  // 既夹带不了内容也放大不了提示词；指令模板与 system 提示是服务端常量。
  // 因此 text 就是"用户控制且进入提示词"的全部内容（O-19）。
  const chars = [...text].length;
  if (chars > limits.maxRequestChars) {
    return jsonError(400, "TEXT_TOO_LONG", `单次最多 ${limits.maxRequestChars} 字。`, {
      limit: limits.maxRequestChars,
    });
  }

  const ip = request.headers.get("CF-Connecting-IP") ?? "unknown";
  const identity: Identity = { clientId, ipHash: await saltedHash(env.IP_HASH_SALT, ip) };

  const denial = await quota.reserve(identity, chars);
  if (denial) {
    return jsonError(429, denial.code, denial.message, {
      remaining: denial.remaining,
      resetAt: denial.resetAt,
    });
  }

  try {
    const translation = await upstream.translate({ text, from, to });
    return json(200, { translation });
  } catch (error) {
    // 上游没给译文：已计字符如数退还，用户不替我们的故障买单。
    await quota.refund(identity, chars);
    return jsonError(502, "UPSTREAM_ERROR", "翻译服务暂时不可用，请稍后再试。");
  }
}

type Parsed =
  | { value: { clientId: string; text: string; from: string | null; to: string } }
  | { message: string };

function parseBody(raw: unknown): Parsed {
  if (typeof raw !== "object" || raw === null) {
    return { message: "请求体应为 JSON 对象。" };
  }

  const body = raw as Record<string, unknown>;
  const clientId = typeof body.clientId === "string" ? body.clientId : "";
  if (!ClientIdPattern.test(clientId)) {
    return { message: "clientId 格式不正确。" };
  }

  if (typeof body.text !== "string" || body.text.length === 0) {
    return { message: "text 必须是非空字符串。" };
  }

  if (typeof body.to !== "string" || body.to.trim().length === 0) {
    return { message: "to（目标语言）不能为空。" };
  }

  const from = typeof body.from === "string" && body.from.trim().length > 0
    ? body.from.trim()
    : null;

  return { value: { clientId, text: body.text, from, to: body.to.trim() } };
}

function json(status: number, payload: unknown): Response {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { "content-type": "application/json; charset=utf-8" },
  });
}

function jsonError(
  status: number,
  code: string,
  message: string,
  extra: Record<string, unknown> = {},
): Response {
  return json(status, { error: { code, message, ...extra } });
}
