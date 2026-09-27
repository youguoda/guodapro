/**
 * IP 只以加盐哈希的形态落 KV：既不能拖库还原，也不能跨部署链接。
 * 盐经 `wrangler secret put IP_HASH_SALT` 注入，与代码永不同处。
 */
export async function saltedHash(salt: string, value: string): Promise<string> {
  const data = new TextEncoder().encode(`${salt}:${value}`);
  const digest = await crypto.subtle.digest("SHA-256", data as BufferSource);
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, "0")).join("");
}
