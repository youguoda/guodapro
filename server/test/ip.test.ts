import { describe, expect, it } from "vitest";
import { normalizeIp } from "../src/ip.js";

describe("IP 规范化：IPv4", () => {
  it("IPv4 原样返回", () => {
    expect(normalizeIp("203.0.113.7")).toBe("203.0.113.7");
    expect(normalizeIp(" 198.51.100.9 ")).toBe("198.51.100.9");
  });
});

describe("IP 规范化：IPv6 /64 聚合", () => {
  it("同一 /64 的各种写法折叠到同一个键", () => {
    const canonical = normalizeIp("2001:db8:85a3:7334::1");
    expect(canonical).toBe("2001:db8:85a3:7334::");

    // 全展开、大写、零压缩、末组长地址——全是同一个 /64。
    expect(normalizeIp("2001:0db8:85a3:7334:0000:0000:0000:0001")).toBe(canonical);
    expect(normalizeIp("2001:DB8:85A3:7334::FFFF:ABCD")).toBe(canonical);
    expect(normalizeIp("2001:db8:85a3:7334:0:0:9abc:def0")).toBe(canonical);
  });

  it("不同 /64 的键不同", () => {
    expect(normalizeIp("2001:db8:85a3:7334::1"))
      .not.toBe(normalizeIp("2001:db8:85a3:7335::1"));
  });

  it("回环地址折叠到全零网段", () => {
    expect(normalizeIp("::1")).toBe("::");
    expect(normalizeIp("::")).toBe("::");
  });

  it("zone 后缀被剥掉（仅本地开发会出现）", () => {
    expect(normalizeIp("fe80::1%eth0")).toBe("fe80::");
  });
});

describe("IP 规范化：IPv4-mapped", () => {
  it("::ffff:a.b.c.d 与明文 IPv4 同桶", () => {
    expect(normalizeIp("::ffff:203.0.113.7")).toBe("203.0.113.7");
  });

  it("嵌入式 IPv4 的大写十六进制前缀同样处理", () => {
    expect(normalizeIp("::FFFF:192.0.2.33")).toBe("192.0.2.33");
  });
});

describe("IP 规范化：兜底", () => {
  it("非 IP 串（缺头时的占位）原样返回", () => {
    expect(normalizeIp("unknown")).toBe("unknown");
  });

  it("解析不了的 IPv6 形态原样返回且结果确定", () => {
    expect(normalizeIp("1:2:3")).toBe("1:2:3");
    expect(normalizeIp("1::2::3")).toBe("1::2::3");
    expect(normalizeIp("::::")).toBe("::::");
    expect(normalizeIp("1.2.3.4:5678")).toBe("1.2.3.4:5678");
  });
});
