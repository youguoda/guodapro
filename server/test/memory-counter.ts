import type { DurableObjectState } from "@cloudflare/workers-types";
import type { Limits } from "../src/limits.js";
import { DailyCounter, type Identity, type QuotaCounter, type QuotaDenial } from "../src/quota.js";
import { FakeDoState } from "./fake-do-state.js";

/**
 * 内存版计数器：与 DurableCounter 同构——同一个 DailyCounter 类跑在
 * 假 state 上，按 UTC 日期分片缓存实例（真实部署是 getByName(日期)）。
 * 单测因此覆盖真实的 DO 逻辑，不碰网络、不碰 Miniflare。
 */
export class MemoryCounter implements QuotaCounter {
  private readonly days = new Map<string, { counter: DailyCounter; state: FakeDoState }>();

  constructor(private readonly limits: Limits) {}

  admit(identity: Identity, chars: number, now: Date): Promise<QuotaDenial | null> {
    return this.day(now).counter.admit(identity, chars, this.limits, now);
  }

  refund(identity: Identity, chars: number, now: Date): Promise<void> {
    return this.day(now).counter.refund(identity, chars, now);
  }

  /** 测试观察口：now 所在日期分片的底层计数。 */
  stateAt(now: Date): FakeDoState {
    return this.day(now).state;
  }

  private day(now: Date) {
    const key = now.toISOString().slice(0, 10);
    let day = this.days.get(key);
    if (!day) {
      const state = new FakeDoState();
      day = { counter: makeDailyCounter(state), state };
      this.days.set(key, day);
    }
    return day;
  }
}

/**
 * 在假 state 上构造 DO 实例。绕开基类构造签名（其 RPC 泛型在 tsc 里会
 * 触发病态展开）：只声明我们用到的形状，ctx 在运行时就是 FakeDoState。
 * 生命周期测试也从这里拿裸 DO 实例。
 */
declare const CounterCtor: new (ctx: DurableObjectState) => DailyCounter;

export function makeDailyCounter(state: FakeDoState): DailyCounter {
  const Ctor = DailyCounter as unknown as typeof CounterCtor;
  return new Ctor(state as unknown as DurableObjectState);
}
