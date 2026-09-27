import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    // 无浏览器、无网络：全部用内存 KV 与可注入上游，与 Worker 运行时解耦。
    include: ["test/**/*.test.ts"],
  },
});
