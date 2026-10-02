import type { DurableObjectState, DurableObjectStorage, SyncKvStorage } from "@cloudflare/workers-types";

/**
 * Durable Object 的假 state：同步 KV 用 Map 接住，闹钟只记录时间。
 * `transactionSync` 直接执行闭包——真 SQLite 的回滚语义只在真实运行时
 * 生效；"任一层超限则三层都不扣"这类原子语义由测试在逻辑层钉死。
 */
export class FakeDoState {
  public readonly kvMap = new Map<string, unknown>();
  public alarmAt: number | null = null;
  public readonly storage: DurableObjectStorage = FakeDoState.makeStorage(this);

  /** 测试观察口：某个键当下的计数值（无则 0）。 */
  count(key: string): number {
    return Number(this.kvMap.get(key) ?? 0);
  }

  private static makeStorage(state: FakeDoState): DurableObjectStorage {
    const kv: SyncKvStorage = {
      get: <T>(key: string): T | undefined => state.kvMap.get(key) as T | undefined,
      put: (key: string, value: unknown): void => {
        state.kvMap.set(key, value);
      },
      delete: (key: string): boolean => state.kvMap.delete(key),
      // 假 state 只存数字计数；泛型签名照抄 SyncKvStorage，取值处自行断言。
      list: <T>(): Iterable<[string, T]> => Array.from(state.kvMap.entries() as Iterable<[string, T]>),
    };

    return {
      kv,
      transactionSync: (closure: () => unknown) => closure(),
      getAlarm: async () => state.alarmAt,
      setAlarm: async (time: number | Date) => {
        state.alarmAt = typeof time === "number" ? time : time.getTime();
      },
      deleteAlarm: async () => {
        state.alarmAt = null;
      },
      deleteAll: async () => {
        state.kvMap.clear();
        state.alarmAt = null;
      },
    } as unknown as DurableObjectStorage;
  }
}

/** 以 DurableObjectState 的面孔使用假 state。 */
export function fakeDoState(): DurableObjectState {
  return new FakeDoState() as unknown as DurableObjectState;
}
