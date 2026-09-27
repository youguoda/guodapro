# dag-skill 评估报告 + 安装记录

**日期**：2026-09-27　**对象**：[RedHeartSecretMan/dag-skill](https://github.com/RedHeartSecretMan/dag-skill)（main @ c967aca 时代拉取，zip 指纹见下）
**结论先行**：**已安装备用，暂不启用**。它是一个高质量的"流程编排"技能，不改代码、不产生优化；对当前的拾语属于超前储备，启用时机是下一个多票批次（v3）。

---

## 1. 它是什么（源码逐行核实）

- **定位**：Approved DAG 的**协调者协议**。用户先批准一张"规格 + 票 + 硬依赖"的票图（以 `.dag/definition-index.json` 机器索引绑定 Git 提交），dag 技能随后担任 Coordinator：计算可执行前沿 → 每票派发一个端到端 Execution Agent（内嵌 `$tdd`/`$codebase-design`/`$code-review`）→ 收三种执行结果（可验收/需决策/外部受阻）→ 串行晋升到集成分支 → 验收并解锁后继票。
- **组成**：`skill/dag/SKILL.md`（235 行合同）+ `references/`（集成转换、定义绑定两份分支契约）+ 两个脚本（`validate_definition_index.py` 索引校验器、`install_runtime_skills.py` 运行时技能安装器）+ 上游测试。
- **运行时技能包**：`code-review` / `tdd` / `codebase-design`——**不在 dag-skill 仓库里**，由安装器从 `mattpocock/skills.git` 的**固定 commit（5b15a47…）+ 树指纹（SHA-256）**拉取。

## 2. 安全审查（第三方技能必经步骤，逐行完成）

- `install_runtime_skills.py`（450 行）：只做 Git fetch 固定 commit + 树指纹核对 + 冲突安全复制。**隔离 Git 环境**（清空调用者 GIT_* 变量、禁 hooks、禁 prompt）、目标存在即保守报错不覆盖、失败保留现场、文件以独占模式写入。无网络回传、无任意命令执行。
- `validate_definition_index.py`（289 行）：纯读校验器——路径安全（拒绝 `..`/`.git`/反斜杠/非 NFC）、JSON 重复键检测、LFS 指针拒绝、blob_oid 与提交树比对。同样无副作用。
- **裁决：可安装**。装前仍采取了两项保守措施：从 codeload zip 取源（不经 git 钩子）、先读后跑。

## 3. 安装记录（本机）

| 项 | 状态 |
|---|---|
| `dag` 技能本体 | ✅ 装至 `C:\Users\14393\.agents\skills\dag`（SKILL.md + references + scripts） |
| `code-review` | ✅ 由 installer 从上游固定 commit 安装（含 agents/ 子目录） |
| `codebase-design` | ✅ 同上 |
| `tdd` | ✅ **保留拾语本地版未动**——installer 的冲突保护实测生效：识别出本机副本非上游指纹后拒绝覆盖并保留现场；本机版有效，恢复备份即可 |
| Python 3.12.10 | ✅ winget 新装（installer 要求 ≥3.12） |
| 沙盒验证 | ✅ 全新 git 仓库 + 最小 definition-index：合法索引 exit 0（输出绑定 JSON），未知字段 exit 1 + 人话错误；**验证器还正确拒绝了带 BOM 的 UTF-8**——与我们仓库踩过的 BOM 坑形成有趣互文 |
| 拾语仓库 | 零改动（验证期间的意外均发生在 Temp） |

## 4. 评估：对拾语的价值与时机

### 4.1 它能带来什么（启用后）
- **流程纪律**：每票独立分支/worktree、TDD+code-review 证据链、有界返工、验收义务、集成分支串行晋升、Run Receipt 断点恢复。
- **三处白送**（票 22 数据树同款逻辑）：设置内搜索遍历数据树即可；深链只传 id；引导复用同一批控件定义。dag 同理：**票图批准后，搜索/调度/评审/恢复都是机制内免费品**。
- **多代理并行**：无硬依赖的票可并发派发（每票独立工作树）。

### 4.2 为什么现在不启用
1. **没有可执行的已批准票图**。v2 的 33 张票全部完成或待人工验收；剩余可开工票（39 EcoPaste 布局、34–38 Glossy 系列）每张都已由本会话的 implement 流程交付或可独立交付——为单票跑 DAG 是仪式大于收益。
2. **合同极重**。SKILL.md 是 235 行的高密度协议（Run Receipt、Definition Checkpoint、Integration Transition、四种 Terminal Outcome），要求 Coordinator 逐字执行。它的收益随票数与并行度增长，单票/串行场景是纯开销。
3. **前置条件未建**：需要先写 `.dag/definition-index.json` 绑定票图——而拾语的 `.scratch` 票天然就是人读格式，索引化是额外语义层。

### 4.3 启用路线（满足任一即值得试）
1. **v3 批次**：若规划 ≥5 张互相有依赖的新票（例如 36 公共通道 + 35 单词卡 + 37 划词徽章天然成图），先写 definition-index，跑一次 3 票最小 DAG 试运行，对比现有 implement 流程的吞吐与缺陷率再定去留。
2. **多代理实验**：想验证并行派发时，选三张写边界互不相交的票（如 34 纯 Core / 39 纯 XAML / 文档票）。

### 4.4 与现有流程的对照
| 维度 | 现有 implement 流程 | + dag |
|---|---|---|
| 单票交付 | ✅ 等价 | 等价（多一层合同） |
| 证据链 | 探针 + 测试 + commit | + 结构化 Review Record / Run Receipt |
| 并行 | 无（单会话串行） | ✅ 原生 |
| 断点恢复 | 会话连续性 | ✅ 协议内建 |
| 上手成本 | 零 | 高（合同学习 + 索引编写） |

## 5. 安装明细（复现用）

- 技能根：`C:\Users\14393\.agents\skills`
- 新增：`dag\`（本体）、`code-review\`、`codebase-design\`（installer 自 `mattpocock/skills.git@5b15a47` 拉取，树指纹校验通过）
- 保留：`tdd\`（拾语本地版；installer 冲突保护实测——识别为"无验证源"并拒绝覆盖）
- Python：3.12.10（winget Python.Python.3.12；installer 下限 3.12）
- 源码留存：`%TEMP%\dag-skill-clean\dag-skill-main\`；沙盒：`%TEMP%\dag-sandbox`

## 6. 触发方式（未来启用时）

对 agent 说"用 dag 技能执行这张票图"并提供 `.dag/definition-index.json` 所在的目标仓库即可；首次运行它会自检 Runtime Skill Bundle（本机已满足）并要求你批准集成发布模式（Local-only / Remote-mirrored）。
