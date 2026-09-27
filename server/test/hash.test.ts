import { describe, expect, it } from "vitest";
import { saltedHash } from "../src/hash.js";

describe("IP 加盐哈希", () => {
  it("同一盐与同一 IP 得到同一摘要", async () => {
    const first = await saltedHash("a-salt", "203.0.113.7");
    const second = await saltedHash("a-salt", "203.0.113.7");

    expect(first).toBe(second);
  });

  it("换盐即换摘要——拖库者拿不到跨部署的可链接身份", async () => {
    const first = await saltedHash("salt-one", "203.0.113.7");
    const second = await saltedHash("salt-two", "203.0.113.7");

    expect(first).not.toBe(second);
  });

  it("不同 IP 得到不同摘要", async () => {
    const first = await saltedHash("a-salt", "203.0.113.7");
    const second = await saltedHash("a-salt", "198.51.100.9");

    expect(first).not.toBe(second);
  });

  it("摘要是 64 位十六进制 SHA-256，且看不出原始 IP", async () => {
    const digest = await saltedHash("a-salt", "203.0.113.7");

    expect(digest).toMatch(/^[0-9a-f]{64}$/);
    expect(digest).not.toContain("203");
    // 盐也不出现在摘要里（哈希当然不含原文，钉住以防有人改成编码）。
    expect(digest).not.toContain("a-salt");
  });
});
