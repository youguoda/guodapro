import type { Env } from "./types.js";

/** 四层配额的数字面。前三个是 Glossy 实证参数，全局封顶为部署留了旋钮。 */
export interface Limits {
  /** 每设备每日字符数。 */
  deviceDaily: number;

  /** 每 IP（加盐哈希）每日字符数。 */
  ipDaily: number;

  /** 全局每日字符数封顶。 */
  globalDaily: number;

  /** 单请求字符上限。 */
  maxRequestChars: number;
}

export const DefaultLimits: Limits = {
  deviceDaily: 20000,
  ipDaily: 30000,
  globalDaily: 200000,
  maxRequestChars: 2000,
};

/** 从环境变量读限额：vars 里没写或写坏了就用默认，部署不改代码也能调。 */
export function readLimits(env: Partial<Env>): Limits {
  return {
    deviceDaily: positiveInt(env.DEVICE_DAILY_LIMIT, DefaultLimits.deviceDaily),
    ipDaily: positiveInt(env.IP_DAILY_LIMIT, DefaultLimits.ipDaily),
    globalDaily: positiveInt(env.GLOBAL_DAILY_LIMIT, DefaultLimits.globalDaily),
    maxRequestChars: positiveInt(env.MAX_REQUEST_CHARS, DefaultLimits.maxRequestChars),
  };
}

function positiveInt(raw: string | undefined, fallback: number): number {
  const value = Number.parseInt(raw ?? "", 10);
  return Number.isFinite(value) && value > 0 ? value : fallback;
}
