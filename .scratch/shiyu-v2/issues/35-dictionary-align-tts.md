# 35 — 体验级：单词词典卡 + 逐句对照 + TTS 朗读

**What to build:** 落实 Glossy 调研（docs/research/2026-09-27-glossy-analysis.md
§9.2）的三个体验补强——拾语翻译"浅层体验"短板，全部纯增量：

- **单词词典卡**：选中文本为单个英文单词（34 的 LanguageGuess + 词形判定）时，
  翻译先出、词典细节两阶段补上——600ms 预算内 `api.dictionaryapi.dev`（免费无
  key）返回则渲染，否则静默放弃。Core 加 `DictionaryCard` 模型 + 可注入
  `IDictionaryApi` 端口（假实现测试）；中文单词走 LLM 词典化 prompt 兜底。
  面板渲染：音标/词性/释义/例句/同义词，紧凑卡式。
- **逐句对照**：Core `SentenceAlign.Pair(original, translated)` 纯函数——按句末
  标点切分、数量相等一一配对、不齐时按比例合并（Glossy merge_meanings 思路），
  单测钉死各种不齐场景；PanelWindow 加"逐句对照"显示开关（默认关）。
  **本地对齐而非让 LLM 输出对齐结构**：对任何后端（含未来公共通道）都成立，
  且不破坏流式。
- **TTS 朗读**：译文区加 🔊 按钮，System.Speech 按译文语言选音色；专用语音
  线程（COM 单元注意项按 Glossy 做法），朗读中再点即停。

**价值:** 语言学习者粘性项；"查词"场景从 LLM 泛答升级为词典精度。

**Blocked by:** 34（语言先验与低温 prompt 是词典触发的判据基础）。

**Status:** ready-for-agent

- [ ] IDictionaryApi 端口 + dictionaryapi.dev 适配器 + 假实现测试
- [ ] 单词判定（单英文词）→ 两阶段词典补全（600ms 预算静默放弃）
- [ ] DictionaryCard 面板渲染（音标/词性/释义/例句/同义词）
- [ ] SentenceAlign.Pair 纯函数 + 不齐合并单测 + 面板逐句对照开关
- [ ] TTS 朗读按钮（按语言选音色、专用线程、朗读中可停）
- [ ] 全量测试绿；翻译探针回归 + 单词/句子两条真后端探针
