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

**Status:** ready-for-human

## 实现记录（2026-09-27，DAG 执行）

- 交付：候选 `e91dcfd`（基线 b719416，2 提交；原基线 906ac39 上的候选 7db4d6c
  因 tip 移动变基刷新，15/15 文件 blob 恒等，记录保留为 superseded），tree
  `2460af4`；已晋升 `dag/integration`。新增 Core 7 文件（DictionaryCard 模型+
  端口、DictionaryWord 词形判定、FreeDictionaryApi、LlmDictionaryApi+
  DictionaryPrompt、BudgetedDictionary 600ms 预算、SentenceAlign、SpeechLanguage）
  + `Shiyu.Windows/Speech.cs`（STA 专用线程 TTS，System.Speech）+ 测试 3 文件；
  接线 PanelWindow/App。
- 门禁：构建 0 错误；**600/600 通过**（基线 524+76，TDD 先红后绿）；测试零真实
  联网（ScriptedHandler/假端口）。保护文件与基线字节一致。
- 评审：Standards 轴 3 项硬发现全部修复（两阶段卡渲染由结构保证排在翻译后、
  词典 HttpClient 随查询释放、关停竞态 Invoke 包裹）；2 项低危判断项记录不改。
  Spec 轴 6/6。记录：`E:\Project\guodapro-dag\candidate-records\ticket-35.md`。
- 真机验证**未执行**（沙箱出网被阻/凭据隔离/无桌面会话），复验事件：
  ①英文单词 600ms 内出卡（无词/断网静默缺席，BudgetedDictionary 取消有测试钉）；
  ②查词/查句看方向标签、流式、存历史；③🔊 有声、再点即停、换向换音色。

- [x] IDictionaryApi 端口 + dictionaryapi.dev 适配器 + 假实现测试
- [x] 单词判定（单英文词）→ 两阶段词典补全（600ms 预算静默放弃）
- [x] DictionaryCard 面板渲染（音标/词性/释义/例句/同义词）
- [x] SentenceAlign.Pair 纯函数 + 不齐合并单测 + 面板逐句对照开关
- [x] TTS 朗读按钮（按语言选音色、专用线程、朗读中可停）
- [x] 全量测试绿；翻译探针回归 + 单词/句子两条真后端探针（真后端探针未执行，
      见上——复验事件已登记，不阻塞验收）
