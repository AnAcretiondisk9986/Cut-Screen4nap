# V1.1

发布日期：2026-11-07

## 概述

V1.1 在 V1.0 三档灭屏功能之上，新增**开机自启（静默启动）**：登录后 Cut-Screen 只在托盘静默就绪，不弹主窗口、不抢焦点、不弹气泡，需要时右键托盘即用。三档行为与 V1.0 完全一致。

## 新增

- 设置页新增「开机自启」分区与开关「开机自动启动（静默，不弹主窗口）」
- 开启后写当前用户 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 运行项，值为 `"<exe 路径>" --silent`；**无需管理员、无需计划任务**，取消勾选即移除
- 新增 `--silent`（兼容 `/silent`）命令行参数：托盘就绪但主窗口不弹出、不抢焦点、不弹崩溃恢复气泡；崩溃恢复 `Snapshot.Recover()` 仍会执行
- 静默启动遇到已有实例时直接退出、不广播唤醒消息，避免登录后重复把主窗口亮出来；手动双击仍唤醒已有实例
- 每次启动自动对齐配置与注册表：exe 搬家会更新运行项路径，运行项被手动删除则回写配置为关（以注册表为准）
- 自检新增第 9 项「开机自启状态读取（只读）」
- 设置写注册表失败时弹提示，不静默吞掉

## 变更

- 版本号 `1.0.0` → `1.1.0`
- 文档补充开机自启的用法、原理与架构说明；包内 `README.txt` 标注 V1.1
- 二进制包改为 `CutScreen-win-x64-V1.1.zip`

三档灭屏、保活、崩溃恢复、DDC/CI、输入检测等行为均不变。

## 使用前提

- Windows 10/11 x64
- .NET 10 Desktop Runtime（或 .NET 10 SDK ≥ 10.0.100）
- 休眠档需要本机启用 hibernate；未启用时程序会提示并尝试 `powercfg /hibernate on`（需管理员，一次即可）
- 开机自启仅对当前用户生效，不需要管理员权限

## 安装

下载 `CutScreen-win-x64-V1.1.zip`，解压到任意目录，保持四个产物文件在同一文件夹，双击 `CutScreen.exe`。

```text
CutScreen.exe  CutScreen.dll  CutScreen.runtimeconfig.json  CutScreen.deps.json
```

单独复制 `CutScreen.exe` 会报「You must install .NET Desktop Runtime」。

开启自启：**设置 → 开机自启 → 勾选 → 保存并关闭**。也可命令行验证 `CutScreen.exe --silent`。

## 校验

| 文件 | SHA-256 |
|---|---|
| `CutScreen-win-x64-V1.1.zip` | `17155a89ae7d3bacb587465c757b71adbe629ef98e5c813c82f6a64df8d4fbba` |

## 已知边界

- 屏幕灭了，显示器待机灯、键盘背光、RGB 仍在，软件收不掉。
- 挂机档默认不锁屏，靠「键鼠已放置」的物理隔离兜底；在意室友动鼠标可在设置里开启锁屏（开启后任意输入只点亮锁屏，仍需输 PIN）。
- 挂机档 DDC/CI 默认关闭：`VCP 0xD6 = 0x04` 深睡可能导致背光叫不醒，先跑一次「检测外接屏 DDC/CI 校准」再开。
- 休眠档只能按电源键唤醒，键鼠叫不醒（S4 特性）。
- 午睡档为纯黑窗口遮光，IPS 内屏会透出深灰；要彻底黑请用挂机档。
- 开机自启为当前用户级（HKCU Run）：切换 Windows 账户不会跟随；移动 exe 后下次启动会自动修正运行项路径。

## 文档

- 使用指南：[docs/guide.md](docs/guide.md)
- 架构说明：[docs/architecture.md](docs/architecture.md)
- 配置速查：[docs/configuration.md](docs/configuration.md)
- V1.0 发行说明：[Releases](https://github.com/AnAcretiondisk9986/Cut-Screen4nap/releases/tag/V1.0)

## 开发声明

本项目全程由 **Tencent Hy-4** 与 **ZCode** 开发。

## License

[Apache-2.0](LICENSE)
