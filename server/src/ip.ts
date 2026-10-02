/**
 * IP 规范化（进哈希之前）：IPv4 原样；IPv6 折叠到 /64——同一 /64 内的
 * 任意地址共享同一份 IP 额度，堵"一个 /64 换无数地址刷 IP 层"（O-19）。
 * 只需要处理 CF-Connecting-IP 可能出现的形态；解析不了的串原样返回，
 * 保持确定性即可（哈希仍可计算，只是不去聚合）。
 */
export function normalizeIp(raw: string): string {
  const value = raw.trim().split("%", 2)[0];
  if (!value.includes(":")) {
    return value;
  }

  const groups = expandIpv6(value);
  if (groups === null) {
    return value;
  }

  // IPv4-mapped（::ffff:a.b.c.d）按 IPv4 记账——与明文 IPv4 同桶，否则
  // 同一台机器换个表示法就能翻倍额度。
  if (groups[5] === 0xffff && groups.slice(0, 5).every((group) => group === 0)) {
    return `${groups[6] >> 8}.${groups[6] & 0xff}.${groups[7] >> 8}.${groups[7] & 0xff}`;
  }

  // 取前 4 组（前 64 位），其余归零；统一渲染成规范压缩写法，等价写法因此同键。
  return renderIpv6([...groups.slice(0, 4), 0, 0, 0, 0]);
}

/** RFC 5952 风格的最长零串压缩（取最长、并列取最前）。 */
function renderIpv6(groups: readonly number[]): string {
  let runStart = -1;
  let bestStart = -1;
  let bestLength = 1;
  for (let index = 0; index < groups.length; index += 1) {
    if (groups[index] === 0) {
      if (runStart < 0) {
        runStart = index;
      }
      if (index - runStart + 1 > bestLength) {
        bestStart = runStart;
        bestLength = index - runStart + 1;
      }
    } else {
      runStart = -1;
    }
  }

  if (bestStart < 0) {
    return groups.map((group) => group.toString(16)).join(":");
  }

  const head = groups.slice(0, bestStart).map((group) => group.toString(16)).join(":");
  const tail = groups.slice(bestStart + bestLength).map((group) => group.toString(16)).join(":");
  return `${head}::${tail}`;
}

/** 展开成 8 组数值；非法返回 null。覆盖 `::` 压缩与嵌入式 IPv4 尾巴。 */
function expandIpv6(raw: string): number[] | null {
  let text = raw;

  // 尾部的嵌入式 IPv4（::ffff:192.0.2.1）先折成两组十六进制。
  const embedded = text.match(/^(.*:)(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})$/);
  if (embedded) {
    const octets = embedded[2].split(".").map(Number);
    if (octets.some((octet) => !Number.isInteger(octet) || octet > 255)) {
      return null;
    }
    text = `${embedded[1]}${((octets[0] << 8) | octets[1]).toString(16)}:${((octets[2] << 8) | octets[3]).toString(16)}`;
  }

  const sections = text.split("::");
  if (sections.length > 2) {
    return null;
  }

  const left = sections[0] === "" ? [] : sections[0].split(":");
  const right = sections.length === 2 && sections[1] !== "" ? sections[1].split(":") : [];
  const groups: number[] = [];
  for (const part of [...left, ...right]) {
    if (!/^[0-9a-fA-F]{1,4}$/.test(part)) {
      return null;
    }
    groups.push(Number.parseInt(part, 16));
  }

  if (sections.length === 2) {
    // `::` 至少要替代一组；两侧合计最多 7 组。
    if (groups.length > 7) {
      return null;
    }
    const zeros = 8 - groups.length;
    return [...groups.slice(0, left.length), ...Array<number>(zeros).fill(0), ...groups.slice(left.length)];
  }

  return groups.length === 8 ? groups : null;
}
