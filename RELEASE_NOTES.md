# V1.0

发布日期：2026-11-06

## 概述

Cut-Screen 是一个 Windows 托盘工具。它解决的是：宿舍里笔记本外接多屏、需要临时补觉或夜里跑下载 / 长任务时，Windows 电源菜单没有「只关显示器、系统继续运行」这一项 —— 睡眠会停掉任务，锁定到点照样睡，而「X 分钟后关闭显示器」没有按钮可以立即触发。

本版本提供三个档位，收尾操作完成后选一档、放好键鼠躺下，不用合盖。

## 新增

- 三档界面：休眠（无任务）、挂机（有任务）、午睡（临时补觉）
- 移动鼠标或按任意键即退出，自动还原设置、主窗口自动回来
- 保活只挂 `PowerRequestSystemRequired`，只保系统不睡，允许屏幕灭
- 午睡档挂 `PowerRequestDisplayRequired` 阻止系统自己把显示器送进 DPMS 待机，避免退出时外接屏链路重训练造成闪烁 / 断连
- 两档都不修改电源计划，改由 60 秒重暗定时器与进程级句柄控屏
- 显示器运行时动态枚举，运行中插拔可补齐 / 移除黑窗口
- 崩溃快照自恢复，强杀进程后下次启动先还原残留设置
- 只读环境自检（显示器枚举、电源方案、空闲时钟、DDC/CI 探测、休眠可用性、配置与快照往返）
- 设置页：进入延时、宽限期、锁屏、DDC/CI、鼠标抖动过滤
- CI：GitHub Actions 在 `windows-latest` 上跑 restore → build → self-test

## 使用前提

- Windows 10/11 x64
- .NET 10 Desktop Runtime（或 .NET 10 SDK ≥ 10.0.100）
- 休眠档需要本机启用 hibernate；未启用时程序会提示并尝试 `powercfg /hibernate on`（需管理员，一次即可）

## 安装

下载 `CutScreen-win-x64-V1.0.zip`，解压到任意目录，保持四个产物文件在同一文件夹，双击 `CutScreen.exe`。

```text
CutScreen.exe  CutScreen.dll  CutScreen.runtimeconfig.json  CutScreen.deps.json
```

单独复制 `CutScreen.exe` 会报「You must install .NET Desktop Runtime」。

## 校验

| 文件 | SHA-256 |
|---|---|
| `CutScreen-win-x64-V1.0.zip` | `136256d645155a9c8495bb0cb5071db71d31dc3ce92ad64f1e245292abba5c40` |

## 已知边界

- 屏幕灭了，显示器待机灯、键盘背光、RGB 仍在，软件收不掉。
- 挂机档默认不锁屏，靠「键鼠已放置」的物理隔离兜底；在意室友动鼠标可在设置里开启锁屏（开启后任意输入只点亮锁屏，仍需输 PIN）。
- 挂机档 DDC/CI 默认关闭：`VCP 0xD6 = 0x04` 深睡可能导致背光叫不醒，先跑一次「检测外接屏 DDC/CI 校准」再开。
- 休眠档只能按电源键唤醒，键鼠叫不醒（S4 特性）。
- 午睡档为纯黑窗口遮光，IPS 内屏会透出深灰；要彻底黑请用挂机档。

## 文档

- 使用指南：[docs/guide.md](docs/guide.md)
- 架构说明：[docs/architecture.md](docs/architecture.md)
- 配置速查：[docs/configuration.md](docs/configuration.md)

## 开发声明

本项目全程由 **Tencent Hy-4** 与 **ZCode** 开发。

## License

[Apache-2.0](LICENSE)
