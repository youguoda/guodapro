import type { Env } from "./types.js";
import { handleTranslate } from "./handler.js";

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

    return new Response(
      JSON.stringify({ error: { code: "NOT_FOUND", message: "接口不存在。" } }),
      { status: 404, headers: { "content-type": "application/json; charset=utf-8" } },
    );
  },
};
