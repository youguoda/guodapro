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

**Status:** done (succession via v3 - see docs/manual-test-v5.md)

## 实现记录（2026-09-27，DAG 执行）

- 交付：候选 `4657c7c`（基线 906ac39，单提交；首轮候选 8eee3f6 因票 34 验收
  移动 tip 而变基刷新，记录保留为 superseded），tree `6ced5d9`；已晋升
  `dag/integration`。九文件：主 `BarWindow.xaml.cs` 329 行 +
  BarCards/BarFilters/BarGroups/BarInteractions(793)/BarKeyHandling/BarPreview
  分区 + BarCard/BarCardViews 辅助类型独立文件。XAML 与 x:Name 绑定零改动。
- 门禁：构建 0 错误（无新警告）；**524/524 通过**（基线含票 34 的 58 项；
  本票零测试增删）；主 ≤400、最大 793 ≤800 ✓；diff 纯度三层证明（行多重集
  1908=1908、行级差分删除 0 行、分区抽查逐字节相同；变基后双向 blob 恒等复核）。
- **协调者侧 25 项窄条真机探针：25/25 通过**（failures:0，于本候选工作树构建
  实测；过程中修正了探针自身的过时宽度常量——票 30-33 已把窄条加宽至 384 DIP，
  非本票行为变化）。
- 评审：Standards 轴 0 硬违规、4 条判断级记录（BarInteractions 793/800 容量
  提示——票 37/39 落地时注意）；Spec 轴满足。记录：
  `E:\Project\guodapro-dag\candidate-records\ticket-40.md`（仓库外）。
- 已知怪样原样搬运并记录：`get => _face;` 异常空白、Rebuild 前缺空行（零改动
  原则）。

- [x] 七个文件切分完成，`BarWindow.xaml.cs` 主文件 ≤ 400 行，无文件 > 800 行
- [x] 行为零变化：全量测试绿（466 基线）；构建无新警告
- [x] 窄条探针**全量回归（25 项）**全过——这是本票唯一的行为验收
- [x] git diff 复核：除文件移动与 partial 声明外，无逻辑增删（评审时抽查
      两个分区的 diff 确认为纯搬运）

**Acceptance succession (2026-10-03, ticket 28):** closed via v3 - machine-testable parts are covered by the probe suite (28 checks, 0 fail) and 938+3 automated tests; human-judgement items live in docs/manual-test-v5.md.
