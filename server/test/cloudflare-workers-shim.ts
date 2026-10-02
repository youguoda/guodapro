/**
 * vitest（Node 环境）里 `cloudflare:workers` 的替身：真实运行时由
 * wrangler 提供。这里只需要基类把 ctx/env 存下来——DailyCounter 的
 * 计数逻辑因此能在测试里跑在假 state 上，与生产同一条代码路径。
 */
export class DurableObject {
  constructor(
    protected readonly ctx: unknown,
    protected readonly env: unknown,
  ) {}
}
