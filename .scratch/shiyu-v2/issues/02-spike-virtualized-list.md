# 02 — 技术验证：虚拟化列表的粘性置顶与逐项动画

**这是一张验证票，不是功能票。**

**What to build:** 一个可滚动的卡片列表，满足三件事同时成立：

1. **虚拟化**——一万条记录下滚动不卡、内存不随条数线性增长；
2. **变高行**——每张卡片高度随内容（文本行数、缩略图高度、文件条数）变化；
3. **粘性置顶**——置顶条目固定在滚动视口顶部，下方内容滚过时不透出。

再叠加第四件：**鼠标悬停时，行内最多 12 个小按钮从右侧挤入**（宽度 0→20px、
缩放 0.9→1、位移 +4→0，约 160ms），移开时反向挤出。

## 为什么必须先验证

WPF 的虚拟化面板**会回收容器**。容器被回收再复用时，上一行遗留的动画状态会带到
新一行上——表现为按钮凭空出现在不该出现的行，或者动画卡在中途。参考实现在 Web 上
不存在这个问题，因为它用的虚拟化库把测量和回收都接管了。

粘性置顶在 WPF 的虚拟化面板里**没有内置支持**，很可能要把置顶区做成独立的非虚拟化
列表叠在滚动列表之上，那又带来两块区域滚动同步与选中态统一的问题。

三件事单独都能做，**同时成立**才是这张票要回答的问题。

**Blocked by:** None — can start immediately.

**Status:** done (succession via v3 - see docs/manual-test-v5.md)

- [ ] 一万条记录下滚动流畅，内存占用不随条数线性增长
- [ ] 行高随内容变化，滚动条位置与实际内容一致，不出现跳动
- [x] 置顶行固定在视口顶部，下方内容滚过时不透出背景
- [x] 悬停动画正确：**快速上下划过多行时，不出现按钮残留在错误行上**
- [x] 快速滚动中途悬停，动画从正确状态开始而非从上一行的中间态继续
- [x] 结论写进票内：用了什么面板/结构、容器回收如何处理、有无性能上限

## Comments

### 交付（2026-09-25）

样例在 `spikes/ListSpike/`，`--selftest <path> [--count N]` 产出报告，无参模式供人工体验。
两轮 Release 自测（1k 与 10k 条，同种子数据，混合文本/图片/文件三类变高卡片，12 个悬停按钮）：

| | 1k 条 | 10k 条 |
|---|---|---|
| 容器累计创建 | 5 | 5 |
| 加载后工作集 / 托管堆 | 156 MB / 2.8 MB | 158 MB / 6.6 MB |
| 滚轮节奏纯布局（每步 120px，不含渲染） | 14.6 ms | 17.8 ms |
| 滚轮节奏最慢单步（含渲染） | 62 ms | 82 ms |
| ExtentHeight 全程漂移 | +14.2% | +14.7% |

**结论：三件事 + 动画可以同时成立，路线选定如下。**

1. **结构**：滚动区用 `ListBox` + `VirtualizingStackPanel`（`Recycling` +
   `ScrollUnit=Pixel` + `CacheLength=1 Page`）；置顶区是**独立非虚拟化
   ItemsControl，堆叠在滚动区上方而非叠压**。不透明背景下两种摆法视觉等价
   （滚动内容都恰好在置底边被裁掉），堆叠则完全免去两区滚动同步。选中态统一靠
   数据上的单一 `IsSelected`（INPC），不依赖两个控件各自的选中模型。
2. **回收处理**：悬停动画全走代码（`TrayStrip` 的 Open/Close/Reset），用
   To-only `DoubleAnimation`（可打断、从当前值续跑），并在回收钩子
   （`DataContextChanged`、`Prepare/ClearContainerForItemOverride`）里
   `BeginAnimation(prop, null)` 撕掉上一行的动画状态。模拟回收（同容器换
   DataContext）与真实回收（滚走再滚回）两条路径实测按钮宽度均回到 0，零残留。
3. **性能上限**：滚轮节奏的纯布局成本约 15~18 ms/步且 1k→10k 几乎持平——
   固定开销主导（VSP 像素滚动的偏移解析与 extent 估算），不是卡片数主导；
   最慢单步（含渲染）60~80 ms，快速连滚会有可感知顿挫。**极端跳滚（拖动
   thumb 穿过万条列表）单步可达 2 s**，是像素滚动 + 变高行下 extent 估算
   修正的固有代价——票 12 做主窗时必须处理（候选：自定义 IScrollInfo、
   拖 thumb 时临时切 item 滚动、或按密度旋钮预估行高锁定 extent）。
4. **Extent 漂移 +14~15%**：估算随实现修正，滚动 thumb 全程会缓调尺寸。
   这是"滚动条跳动"的真实来源之一，是否可接受是产品决定。

内存一项已证（不随条目数线性增长，容器只建了 5 个）；流畅与无跳动两项
**待人工**：跑无参模式滚一遍、盯 thumb，再决定前两条是否可勾。

工程坑三条，票 12/13 直接受益：

- 本机的 PresentationBuildTasks 生成的 `App.g.cs` 缺 `InitializeComponent`，
  Application 级资源根本不会加载——窗口资源 + 元素树查找替代，别在 App.xaml 放东西；
- 动画等待必须按**合成帧数**计（`CompositionTarget.Rendering` 计数），
  `Task.Delay` 会读到冻结的渲染钟，把已打开的托盘误报为 0；
- 性能测量要复跑防污染：一轮 1k Release 被后台负载拖慢 7 倍，复跑即恢复。

**Acceptance succession (2026-10-03, ticket 28):** closed via v3 - machine-testable parts are covered by the probe suite (28 checks, 0 fail) and 938+3 automated tests; human-judgement items live in docs/manual-test-v5.md.
