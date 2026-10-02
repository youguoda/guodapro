# 08 — 首次翻译路径：自备密钥优先

**来源：** 优化报告 O-04、O-14；判断报告 P0-3；ADR-0009
**Blocked by:** 07
**Status:** ready-for-agent

**What to build:**
- 公共通道在上线条件满足前不可选（引导与设置里显示"即将推出"）。已选择公共通道的老用户：如果配置了自备密钥就改用它，否则显示配置引导卡；只提示一次。
- 引导的翻译步默认自备密钥：提供国内可直连的服务商预设（依据核实过的预设清单），选中即填好地址和模型，并给出"申请密钥"链接；"测试连接"能区分密钥错误、地址错误、超时；允许"稍后配置"。
- 没配置翻译服务时，面板显示"去配置翻译服务"的引导卡，不显示异常文本。
- 管理窗的批量翻译与面板共用后端工厂；Agent 动作在没有自备密钥时禁用，并说明原因。批量翻译结束时报告"成功 n / 失败 m"。

**已核实的预设数据**（2026-10-01 调研，全部出自官方文档；完整依据在 `E:\Project\guodapro-review\provider-presets.md`）：
- 下拉顺序：阿里云百炼 `qwen-flash`（默认，唯一不加额外字段就不思考，0.15/1.5 元每百万，北京新用户 90 天 100 万 token 免费、免实名）→ DeepSeek `deepseek-flash`（**须加** `"thinking":{"type":"disabled"}`；旧名 `deepseek-chat` 已停用）→ 智谱 `glm-4-flash-250414`（永久免费、非推理，**temperature ≤ 1**）→ 硅基流动 `deepseek-ai/DeepSeek-V4-Flash`（**须加** `"enable_thinking": false`）。Kimi 放"更多"且**不发送 temperature**（固定值，传错报错）；火山方舟不进预设（model 字段未核实）。
- **因此后端要支持**（`OpenAiCompatibleBackend`）：预设可附加请求体顶层字段（关思考）；temperature 按预设限幅或不发送；百炼的 403 `AllocationQuota.FreeTierOnly` 翻成"免费额度已用完"。SSE 解析已只读 `delta.content`（`reasoning_content` 天然被忽略），补一个单测钉住。
- C# 预设清单（含 ExtraBody / MaxTemperature / SendTemperature 字段）见调研文件 §4 的 JSON。

**验收：**
- [ ] 全新隔离数据目录走完引导（全默认）后翻译一个英文单词：面板只会显示译文或配置引导卡，不出现异常文本
- [ ] 测试连接三种错误分别给出人话结果
- [ ] 没有密钥时 Agent 按钮禁用并带说明
