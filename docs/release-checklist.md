# 拾语发布检查清单

每次打 tag 发布前逐项过一遍。来源：Glossy 调研红旗教训
（docs/research/2026-09-27-glossy-analysis.md §7、票 38）。

## 版本与源码一致性（票 38 守则）

- [ ] tag 名与 `AssemblyVersion`/csproj 版本完全一致（CI 已校验，人工复核）
- [ ] release notes 里描述的**每个功能**，都能在**这个 tag 的源码**里指出来源
      文件/提交——描述超前于源码 = 红旗（Glossy v1.5.x 事故）
- [ ] CHANGELOG 已更新且与 release notes 一致，无 "TODO: translate" 残留

## 构建与产物

- [ ] `dotnet test Shiyu.sln` 全绿（当前基线 466+）
- [ ] release 构建来自 CI，不来自本地 bin 拷贝
- [ ] 产物附 SHA256SUMS
- [ ] 更新通道（票 28）的 feed 指向新版本，老版本升级路径实测一次

## 内容

- [ ] release notes 三段结构：新功能 / 修复 / 已知问题
- [ ] 隐私声明无变化时复核仍成立（"剪贴板历史不出机器"）
- [ ] 截图与实际界面一致（UI 大改后必须重截）
