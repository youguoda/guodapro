import type { Env } from "./types.js";

/** 配额与限速的数字面。前三个是 Glossy 实证参数，其余为部署留了旋钮。 */
export interface Limits {
  /** 每设备每日字符数。 */
  deviceDaily: number;

  /** 每 IP（加盐哈希）每日字符数。 */
  ipDaily: number;

  /** 全局每日字符数封顶。 */
  globalDaily: number;

  /** 单请求字符上限。 */
  maxRequestChars: number;

  /** 每 IP（IPv6 按 /64）每分钟请求次数上限（O-19 突发限速）。 */
  ipPerMinute: number;
}

export const DefaultLimits: Limits = {
  deviceDaily: 20000,
  ipDaily: 30000,
  globalDaily: 200000,
  maxRequestChars: 2000,
  // 60/分≈每秒一次：与客户端串行批量翻译的节奏（逐条等译文回来）匹配，
  // 正常人手点远远用不满，又把脚本突发压在一个 IP 每分钟 6 万字的
  // 可控范围里；批量重的用户可在 vars 里调高。
  ipPerMinute: 60,
};

/** 从环境变量读限额：vars 里没写或写坏了就用默认，部署不改代码也能调。 */
export function readLimits(env: Partial<Env>): Limits {
  return {
    deviceDaily: positiveInt(env.DEVICE_DAILY_LIMIT, DefaultLimits.deviceDaily),
    ipDaily: positiveInt(env.IP_DAILY_LIMIT, DefaultLimits.ipDaily),
    globalDaily: positiveInt(env.GLOBAL_DAILY_LIMIT, DefaultLimits.globalDaily),
    maxRequestChars: positiveInt(env.MAX_REQUEST_CHARS, DefaultLimits.maxRequestChars),
    ipPerMinute: positiveInt(env.IP_PER_MINUTE_LIMIT, DefaultLimits.ipPerMinute),
  };
}

function positiveInt(raw: string | undefined, fallback: number): number {
  const value = Number.parseInt(raw ?? "", 10);
  return Number.isFinite(value) && value > 0 ? value : fallback;
}
