# ADR-0006：技术栈

- 状态：**已被 ADR-0008 取代**（2026-09-22）。下文保留原貌以存证决策当时的推理与信息条件。
- 日期：2026-09-21

## 背景

前五个 ADR 累积出的硬性要求：不接受焦点的置顶窗口、光标旁定位（多显示器
per-monitor DPI）、两个全局热键、剪贴板监听、`SendInput` 取词、排除标记识别、
SQLite + 全文搜索 + 磁盘 blob、缩略图、流式 HTTP、托盘常驻、以及一个带缩略图
与多维筛选的"精美"历史面板。

用户反复强调"占用系统内存小、反应快、启动快、小而快、小而精美"，
并将选型判断委托给本次评审。

### 评估中发现的两处简化

1. **拖到资源管理器不需要 OLE 虚拟文件。** 原以为需实现 file promise
   （`CFSTR_FILEDESCRIPTOR`/`CFSTR_FILECONTENTS`）。但 ADR-0004 已确定图片原图
   以文件形式存于磁盘，故直接拖已存在的文件路径（`CF_HDROP`）即可。
   该项从"手写 COM"降级为"几行代码"，且各技术栈均然。
2. **"启动快"的权重应下调。** 托盘常驻程序的冷启动每日仅发生一次（登录时）。
   用户每日感知的是**热路径**——按下热键到面板出现。真正的指标是热路径延迟
   与常驻内存，而非冷启动。

## 决策

**C++ / Qt 6（Widgets）。**

## 理由

1. 常驻内存是用户唯一反复强调的指标，C++/Qt 在此项上优于 .NET/WPF，
   远优于 Tauri（WebView2 多进程）。
2. 本应用约半数工作量是 Win32 管道：`WS_EX_NOACTIVATE` 窗口、`RegisterHotKey`、
   `AddClipboardFormatListener`、`SendInput`、排除标记。在 C++ 中均为一等公民，
   无需跨越 P/Invoke 或 FFI。
3. 用户机器已具备 Qt、CLion、OpenCV、HALCON 工具链，个人项目的首要风险是
   **做不完**，熟悉的工具链使该风险最低。
4. 非 Win32 部分 Qt 均有一方支持：`QSystemTrayIcon`、`QtSql`(SQLite)、
   `QNetworkAccessManager`（流式）、Qt 6 的 per-monitor DPI，
   以及 `Qt::WindowDoesNotAcceptFocus`（映射至 `WS_EX_NOACTIVATE`）。

## 后果

- **安装包将包含数十 MB 的 Qt DLL。** 用户确认安装包体积非约束项。
- **"精美"需要额外投入。** Qt Widgets 默认观感陈旧，需认真编写 QSS。
  这是本方案最实在的成本，WPF 在此项上明显更省力。
- **Qt 许可**：个人自用 + 动态链接适用 LGPL；若日后闭源商用需重新评估。

> **待验证**：本决策中关于各栈常驻内存与启动耗时的量级判断均为先验，
> **未经一手来源或本地实测核实**（用户选择跳过技术调研）。

## 被推翻的备选

- **C# / .NET 8 + WPF** — UI 开发效率与"精美"完成度明显更高，剩余 Win32 需求
  全是成熟 P/Invoke。仅因常驻内存高于 C++ 而落选。**若日后"做完"的压力
  超过"省内存"，这是第一顺位的回头选项。**
- **Tauri v2** — UI 人机工程最佳，但 WebView2 多进程使常驻内存在候选中最差，
  且应用有半数是原生管道活，WebView 反成阻碍。
- **Qt Quick / QML** — 观感优于 Widgets，但引入 GPU 场景图与另一门语言，与"小"冲突。
- **Rust + windows-rs + 轻量 GUI** — 约束项上理论最优，但完成风险最高。
