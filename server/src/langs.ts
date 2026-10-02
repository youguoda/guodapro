/**
 * 语言白名单（O-19）：`to`/`from` 从自由文本收窄成封闭集合——它们会原样
 * 进入提示词，白名单既是提示词注入的闸，也是额度绕过的闸（把要处理的
 * 内容塞进 `to` 不再可能）。客户端送英文语言名或语言代码；大小写与常见
 * 别名都折叠到唯一的规范名，规范名才是提示词里出现的字符串。
 */
const Languages: ReadonlyArray<{
  readonly name: string;
  readonly aliases: readonly string[];
}> = [
  {
    name: "Chinese",
    aliases: [
      "zh", "zh-cn", "zh-hans", "zh-hans-cn", "cmn", "mandarin", "mandarin chinese",
      "simplified chinese", "chinese (simplified)", "chinese(simplified)", "chinese simplified",
    ],
  },
  {
    name: "Traditional Chinese",
    aliases: [
      "zh-tw", "zh-hant", "zh-hant-tw", "zh-hk", "traditional chinese",
      "chinese (traditional)", "chinese(traditional)", "chinese traditional",
    ],
  },
  { name: "English", aliases: ["en", "en-us", "en-gb", "american english", "british english", "english (us)", "english (uk)"] },
  { name: "Japanese", aliases: ["ja", "ja-jp", "jp"] },
  { name: "Korean", aliases: ["ko", "ko-kr", "kr"] },
  { name: "French", aliases: ["fr", "fr-fr", "fra"] },
  { name: "German", aliases: ["de", "de-de", "deu"] },
  { name: "Spanish", aliases: ["es", "es-es", "spa"] },
  { name: "Portuguese", aliases: ["pt", "pt-pt", "pt-br", "por", "brazilian portuguese", "portuguese (brazil)"] },
  { name: "Italian", aliases: ["it", "it-it", "ita"] },
  { name: "Russian", aliases: ["ru", "ru-ru", "rus"] },
  { name: "Ukrainian", aliases: ["uk", "uk-ua", "ukr"] },
  { name: "Arabic", aliases: ["ar", "ar-sa", "ara"] },
  { name: "Hindi", aliases: ["hi", "hi-in", "hin"] },
  { name: "Indonesian", aliases: ["id", "id-id", "ind", "bahasa indonesia"] },
  { name: "Vietnamese", aliases: ["vi", "vi-vn", "vie"] },
  { name: "Thai", aliases: ["th", "th-th", "tha"] },
  { name: "Turkish", aliases: ["tr", "tr-tr", "tur"] },
  { name: "Polish", aliases: ["pl", "pl-pl", "pol"] },
  { name: "Dutch", aliases: ["nl", "nl-nl", "nld"] },
  { name: "Czech", aliases: ["cs", "cs-cz", "ces", "cze"] },
  { name: "Swedish", aliases: ["sv", "sv-se", "swe"] },
  { name: "Danish", aliases: ["da", "da-dk", "dan"] },
  { name: "Finnish", aliases: ["fi", "fi-fi", "fin"] },
  { name: "Norwegian", aliases: ["no", "no-no", "nb", "nb-no", "nob", "nor", "norwegian bokmål", "norwegian bokmal"] },
  { name: "Greek", aliases: ["el", "el-gr", "ell", "gre"] },
  { name: "Hebrew", aliases: ["he", "he-il", "heb"] },
  { name: "Hungarian", aliases: ["hu", "hu-hu", "hun"] },
  { name: "Romanian", aliases: ["ro", "ro-ro", "ron", "rum"] },
  { name: "Bulgarian", aliases: ["bg", "bg-bg", "bul"] },
  { name: "Catalan", aliases: ["ca", "ca-es", "cat"] },
  { name: "Croatian", aliases: ["hr", "hr-hr", "hrv"] },
  { name: "Slovak", aliases: ["sk", "sk-sk", "slk", "slo"] },
  { name: "Serbian", aliases: ["sr", "sr-rs", "srp"] },
  { name: "Malay", aliases: ["ms", "ms-my", "msa"] },
];

/** 全部规范名——测试用它对表，README 的支持列表也从这里数。 */
export const CanonicalLanguageNames: readonly string[] = Languages.map((entry) => entry.name);

const Lookup = new Map<string, string>();
for (const { name, aliases } of Languages) {
  Lookup.set(name.toLowerCase(), name);
  for (const alias of aliases) {
    Lookup.set(alias, name);
  }
}

/**
 * 把客户端送来的语言名折叠成规范名；不在集合里返回 null（调用方回 400）。
 * 只做 trim/小写/空白折叠就够——集合封闭，查表不中即拒绝，无须更重的
 * 规范化，天然挡掉超长串与花式注入写法。
 */
export function normalizeLanguage(raw: string): string | null {
  return Lookup.get(raw.trim().toLowerCase().replace(/\s+/g, " ")) ?? null;
}
