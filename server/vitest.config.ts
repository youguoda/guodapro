import { defineConfig } from "vitest/config";

// workers-types 收窄了 ImportMeta（无 url），配置文件在 Node 里跑——显式
// 取回标准字段来定位测试替身。
const moduleUrl = (import.meta as unknown as { url: string }).url;

export default defineConfig({
  test: {
    // 无浏览器、无网络：DO 跑在假 state 上、上游可注入，与 Worker 运行时解耦。
    include: ["test/**/*.test.ts"],
  },
  resolve: {
    alias: {
      // Node 里没有 cloudflare:workers（那是 wrangler 运行时模块）：
      // 指到测试替身上，DailyCounter 的逻辑在生产与测试走同一条路。
      "cloudflare:workers": new URL("./test/cloudflare-workers-shim.ts", moduleUrl).href,
    },
  },
});
