# 构建与发布指南 | Build & Release Guide

> 本文档说明从源码到本机安装、再到 GitHub Release 的完整流程，以及每一步的注意事项。
> This document covers the full path from source code to a locally installed EXE and to a GitHub Release, with the caveats of each step.

## 输出目录速查 | Output directories at a glance

| 目录 Directory | 由谁生成 Produced by | 用途 Purpose |
|---|---|---|
| `电脑使用时间统计/bin/Debug/net9.0-windows/` | `dotnet build`（Rider 构建按钮 / Build button） | 开发调试：exe 只是启动器，需同目录的 dll、`.deps.json` 等配套文件；未优化、依赖本机 .NET 9 运行时。Development & debugging: the EXE is just a launcher plus sibling DLLs, unoptimized, requires the local .NET 9 runtime. |
| `电脑使用时间统计/bin/Release/net9.0-windows/win-x64/` | `dotnet publish` 的中间产物 | 发布"施工场地"：先在这里组装优化后的文件，再打成单文件。Intermediate output: publish assembles optimized files here before packing into a single file. |
| `电脑使用时间统计/bin/publish/` | `dotnet publish -o publish` | **最终成品**：框架依赖单文件 EXE（约 400 KB）。**The final artifact**: a framework-dependent single-file EXE (~400 KB). |

`bin/` 里同时会有 `*.pdb` 文件——那是调试符号（崩溃堆栈行号映射），运行时不需要，不随发布分发。
`*.pdb` files inside `bin/` are debug symbols (crash stack-trace line mapping); they are not needed at runtime and are not shipped.

三个目录都是再生成物，可随时整目录删除（下次构建自动重建），仓库不跟踪它们。
All three are regenerable—safe to delete at any time; they are never committed.

## 流程 A：本机更新小组件本体 | Flow A: update the locally installed widget

适用于把最新代码装到固定位置（示例用 `D:\电脑使用时间统计.exe`，可任意）。
For installing the latest build to a fixed location (example uses `D:\电脑使用时间统计.exe`; any path works).

```bash
# 1. 结束正在运行的小组件（否则 EXE 被进程锁定，复制会失败）
# 1. Stop the running widget first, or the EXE will be locked
taskkill /IM 电脑使用时间统计.exe /F

# 2. 发布框架依赖单文件
# 2. Publish the framework-dependent single file
dotnet publish 电脑使用时间统计/电脑使用时间统计.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o 电脑使用时间统计/bin/publish

# 3. 覆盖安装（路径自定），然后重新启动
# 3. Overwrite the installed EXE (path up to you), then start it again
copy /Y 电脑使用时间统计/bin/publish/电脑使用时间统计.exe D:\电脑使用时间统计.exe
```

**注意事项 | Caveats**

- `dotnet build` **永远不会**自动更新 `bin/publish/`，也永远不会触碰已安装的 EXE——更新本体必须显式执行上面的 publish + copy。
  `dotnet build` **never** updates `bin/publish/` and **never** touches the installed EXE—updating the installed copy always requires the explicit publish + copy above.
- 发布前记得改 `电脑使用时间统计.csproj` 里的 `<Version>`（本项目约定：小改动次版本 +1，如 2.5 → 2.6），构建后右键 EXE 属性 → 详细信息 可核对。
  Bump `<Version>` in the csproj before publishing (convention: minor +1 per change, e.g. 2.5 → 2.6); verify via EXE Properties → Details.
- 数据目录 `D:\电脑使用时间统计数据\` 与程序目录无关，覆盖升级不影响历史统计。
  The data folder `D:\电脑使用时间统计数据\` is independent of the EXE; upgrading never touches your statistics.
- 开机自启指向 EXE 的**完整路径**：路径不变则升级后自动生效；换路径后运行一次新 EXE 即自动刷新注册表项。
  Autostart points to the **full path** of the EXE: same path, nothing to do after upgrading; moved path, just run the new EXE once to refresh the registry entry.
- 本机版是框架依赖（约 400 KB，需 .NET 9 桌面运行时）；想要零依赖单文件，把 `--self-contained false` 改为 `true`（约 150 MB）。
  The local build is framework-dependent (~400 KB, needs the .NET 9 Desktop Runtime); switch `--self-contained false` to `true` for a zero-dependency file (~150 MB).

> 项目的 GitHub Release 由维护者推送版本标签后经 Actions 自动发布，工作流定义（含注释）见 [.github/workflows/release.yml](.github/workflows/release.yml)。
> GitHub Releases are published automatically by Actions when the maintainer pushes a version tag; see [.github/workflows/release.yml](.github/workflows/release.yml) for the annotated workflow definition.

## 单实例与测试 | Single instance & testing

- 程序通过命名互斥体保证全局单实例：已有实例运行时再次启动，第二实例立即以退出码 0 结束并唤出已运行的窗口——双开测试看到"秒退"是预期行为。
  A named mutex enforces a single instance: launching a second copy makes it exit with code 0 immediately while summoning the running window—a "quick exit" during double-launch testing is expected.
- 程序无界面退出入口（刻意防呆），结束它只能用任务管理器；强杀最多丢最近 30 秒统计（数据每 30 秒节流落盘）。
  There is deliberately no in-app exit; use Task Manager to stop it. A force-kill loses at most the last 30 s of statistics (data flushes on a 30 s throttle).
