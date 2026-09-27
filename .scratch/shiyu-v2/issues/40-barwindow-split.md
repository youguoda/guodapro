# 40 — BarWindow 拆分：按分区切地块，行为零变化

**What to build:** 把 `BarWindow.xaml.cs`（现约 2972 行）按其既有的注释分区
切成多个 **partial class 文件**，行为、XAML、公共接口零变化。这是纯结构
重构——票 39（布局重构）、票 37（划词徽章）以及后续所有要动窄条的票，
都应落在这块切好的地上，而不是继续往一个近三千行的文件里加。

## 为什么现在做

TieZ 调研（docs/research/2026-09-26-tiez-clipboard-analysis.md §4.2）的教训：
9 个 50–113KB 的巨型文件是那个项目维护性崩塌的直接原因。拾语的
`BarWindow.xaml.cs` 已经 2972 行，且票 39 预计再加 400–600 行。**在加之前
切，回归网（25 项窄条探针）现成；在加之后切，切的是不认识的代码。**

## 切法（按现有注释分区，XAML 不动）

| 新文件 | 内容（对应现有分区） | 预计行数 |
|---|---|---|
| `BarWindow.xaml.cs` | 构造、字段、显示/隐藏（Summon/Dismiss/Toggle）、几何记忆 | ~350 |
| `BarCards.cs` | BarCard 构建（CardFor/Append/Rebuild/Reload）、空态、计数 | ~350 |
| `BarFilters.cs` | 搜索去抖、类型/子类型/标签/收藏过滤、分组 chips 与溢出 | ~500 |
| `BarGroups.cs` | 分组菜单/选择器/归组执行、GroupManager 入口 | ~300 |
| `BarInteractions.cs` | 卡片按压/拖出、右键菜单、悬停动作执行（ExecuteAction 家族）、备注编辑 | ~700 |
| `BarKeyboard.cs`（App 侧，注意与 Core 同名区分或命名 BarKeyHandling.cs） | 键盘模型、键帽提示、Esc 栈 | ~350 |
| `BarPreview.cs` | 预览编排（policy/tick/锚点/连接曲线联动） | ~300 |

约束：
- **partial class 拆分，XAML 与 x:Name 绑定不动**——这是风险最小的切法；
  类名/命名空间/可见性不变，App.xaml.cs 等调用方零改动。
- 私有字段归属其主用分区；跨分区共用的（如 `_settings`、`_selected`）留
  在主文件。
- `BarCard`/`FileRow`/`BarCardContainer` 等辅助类型若顺手，可移到独立文件，
  但不属于本票验收必须项。

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] 七个文件切分完成，`BarWindow.xaml.cs` 主文件 ≤ 400 行，无文件 > 800 行
- [ ] 行为零变化：全量测试绿（466 基线）；构建无新警告
- [ ] 窄条探针**全量回归（25 项）**全过——这是本票唯一的行为验收
- [ ] git diff 复核：除文件移动与 partial 声明外，无逻辑增删（评审时抽查
      两个分区的 diff 确认为纯搬运）
