import type { Env } from "./types.js";
import { handleTranslate } from "./handler.js";

// wrangler 从入口导出里发现 Durable Object 类（O-19 原子配额计数）。
export { DailyCounter } from "./quota.js";

/**
 * 拾语公共翻译通道（票 36）：客户端零密钥，凭据只活在 Worker 的
 * secret 里。契约与部署步骤见 server/README.md。
 */
export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname === "/translate") {
      return handleTranslate(request, env);
    }

    if (url.pathname === "/health") {
      return health(env);
    }

    return new Response(
      JSON.stringify({ error: { code: "NOT_FOUND", message: "接口不存在。" } }),
      { status: 404, headers: { "content-type": "application/json; charset=utf-8" } },
    );
  },
};

/**
 * 部署探针：缺配置必须在这里亮出来（O-19 验收项）。只报缺失项的名字，
 * 绝不回显任何 secret 的值。
 */
function health(env: Env): Response {
  const problems: string[] = [];
  if (typeof env.IP_HASH_SALT !== "string" || env.IP_HASH_SALT.trim().length === 0) {
    problems.push("IP_HASH_SALT 未设置");
  }
  if (typeof env.ZHIPU_API_KEY !== "string" || env.ZHIPU_API_KEY.length === 0) {
    problems.push("ZHIPU_API_KEY 未设置");
  }
  if (!env.QUOTA_COUNTER) {
    problems.push("QUOTA_COUNTER 绑定缺失");
  }

  if (problems.length > 0) {
    return new Response(
      JSON.stringify({ status: "error", problems }),
      { status: 503, headers: { "content-type": "application/json; charset=utf-8" } },
    );
  }

  return new Response(
    JSON.stringify({ status: "ok" }),
    { status: 200, headers: { "content-type": "application/json; charset=utf-8" } },
  );
}
