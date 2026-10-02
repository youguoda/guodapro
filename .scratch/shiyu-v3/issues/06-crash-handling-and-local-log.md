# 06 — 全局异常处理、本地日志、静默 catch 治理

**来源：** 优化报告 O-05、O-24
**Blocked by:** 03
**Status:** ready-for-agent

**What to build:**
- `DispatcherUnhandledException`、`TaskScheduler.UnobservedTaskException`、`AppDomain.UnhandledException` 三个处理器。
- Core 里一个 `Log` 门面：参数只接受事件枚举和异常，**不接受任意字符串**，从类型上杜绝把剪贴板内容、译文、密钥写进日志。
- 日志写到 `%LOCALAPPDATA%\Shiyu\logs\shiyu-yyyyMMdd.log`，保留 7 天，单文件不超过 1 MB。
- UI 线程上的非致命异常：记日志，托盘提示一次，继续运行。
- 4 个 `async void`（`App.ShowPanel`、`LibraryWindow` 两处、`PanelWindow.OnSwapDirection`）、更新窗"安装"、`AppSettings.Save` 都要有保护。
- `OnExit` 先归还划词借走的剪贴板，再做其他清理。
- 45 处静默 `catch (Exception)` 逐一归类：预期且无害的收窄类型并注释；预期外的记日志；影响用户的在界面上说明。
- CI 检查：`catch (Exception)` 块内必须有 `Log.` 调用或 `// expected:` 注释。

**验收：**
- [ ] 调试构建里的隐藏命令在 `DispatcherTimer.Tick` 中抛异常：进程存活，日志多一行，托盘提示一次
- [ ] CI 静态检查为绿
- [ ] 日志文件中搜不到任何测试用的剪贴板文本
