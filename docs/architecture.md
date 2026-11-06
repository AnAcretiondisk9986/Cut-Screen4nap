# 架构说明

## 目标与约束

单文件 WinForms 托盘程序，.NET 10 / Windows x64。核心约束三条：

1. **只保系统不睡，绝不保屏幕不亮** —— 保活请求只挂 `SystemRequired`，挂机档；午睡档额外挂 `DisplayRequired` 是为了阻止系统自己把屏送进待机
2. **不改任何持久化电源设置** —— 早期版本收紧「关闭显示器」超时导致外接屏断连闪烁，现改为进程级句柄 + 重暗定时器
3. **崩溃可恢复** —— 凡是改了全局状态的档位都落盘快照，下次启动先还原

## 状态机

```
[点选档位]  Request(mode)
      ↓
  Countdown（默认 20 秒，可取消）
      ↓  _ui(FinishCountdown)
  Entering
      ↓ 分档进入
  Active ──任意输入 / 托盘退出 / 窗口关闭──→ Exit → TeardownResources
```

`ModeController` 的线程模型：所有状态迁移都经 `_ui` 封送到 UI 线程。计时器回调、输入监视器回调都先 `_ui(...)` 再干活 —— 黑窗口是 WinForms `Form`，跨线程创建 / 销毁会白屏。唯一例外是 `InputWatcher.Arm` / `GetLastInputInfo` 这类纯 Win32 调用（线程安全）。

`_generation` 守卫：每次进入 / 退出递增，作废已排队但尚未执行的重暗动作，否则退出后一条排队的 `DpmsOff` 会把刚唤醒的屏又关掉。

三档形状不同：

- **休眠**：一次性动作（fire-and-forget），无状态、无快照、无还原
- **挂机 / 午睡**：有状态会话（enter/exit 对），共享外壳（GUI、托盘、可取消延时、`GetLastInputInfo` 轮询、快照管理），差异只填两个 delegate：显示层怎么做、电源层要不要保活

## 分层

| 文件 | 职责 |
|---|---|
| `Program.cs` | 单实例互斥体、`SELFTEST` 编译开关入口 |
| `TrayAppContext.cs` | `ApplicationContext`、托盘、UI 线程封送器 `MarshalToUi`、图标缓存 |
| `MainForm.cs` | 三档卡片、状态行、可取消倒计时 |
| `SettingsForm.cs` | 设置页（纯 `AutoSize` 布局） |
| `ModeController.cs` | 状态机、重暗定时器、快照、崩溃自恢复 |
| `Native.cs` | Win32 / DXVA2 / powrprof P/Invoke 集中层 |
| `DisplayController.cs` | 黑窗口 / DPMS / DDC/CI 三套显示层 + 校准 |
| `WakeLock.cs` | `PowerCreateRequest` 保活 + `SetThreadExecutionState` 回退 |
| `InputWatcher.cs` | `GetLastInputInfo` 轮询 + 可选键盘钩子做抖动过滤 |
| `Config.cs` | 设置与快照的 JSON 落盘 |
| `SelfTest.cs` | `SELFTEST` 编译开关下的只读自检 |

## 显示层三种实现

| 方案 | 真黑 | 不触发重枚举 | 覆盖内屏 | 用途 |
|---|---|---|---|---|
| `SC_MONITORPOWER` 广播 | 是 | 否（部分 DP / 扩展坞会掉链） | 是 | 挂机档主路径 |
| DDC/CI 写 `VCP 0xD6` 关背光 | 是 | 是 | 否（eDP 不支持） | 挂机档外接屏可选 |
| 每屏一个纯黑全屏窗口 | 否（IPS 透深灰） | 是 | 是 | 午睡档 |

关键实现细节：

- DPMS 广播用 `SendMessageTimeout(..., SMTO_ABORTIFHUNG, 3000)` 而非 `SendMessage`，一个卡死的顶层窗口不能把本进程挂住
- 黑窗口用 `SetWindowPos` 按 `rcMonitor` 物理像素定位，绕开 WinForms DPI 虚拟化；需声明 `PerMonitorV2`
- `ShowCursor(false)` 只调一次，退出时对称加一；循环压到 -N 会让光标永久消失
- DDC/CI 退出顺序：先 `DpmsOn()` 唤醒视频链路 → 等 150 ms → 写回 `0x01`。深睡中的显示器收不到 VCP 写回
- 失败的物理句柄立即 `CloseHandle`，成功的留给退出时写回

## 输入检测

`GetLastInputInfo` 轮询系统级「最后输入时间戳」，鼠标移动 / 任意按键都会改变它，零权限、零注入。

- 进入档位后 3 秒宽限期才 `Arm()` 取基线，吃掉「放置键鼠 / 躺下」的最后几次输入
- tick 回绕（约 49.7 天一次）时重设基线而非误判为输入
- 可选「忽略光学鼠标微小抖动」：额外在专用 STA 消息循环线程装 `WH_KEYBOARD_LL`，用 `GetCursorPos` 位移判断鼠标，钩子未就绪时自动退化为「任意输入即退出」，绝不吞掉真实输入

## 保活

首选 `PowerCreateRequest`（句柄式、不绑线程、`powercfg /requests` 可见），失败回退 `SetThreadExecutionState` 常驻线程。

- 挂机档：只挂 `SystemRequired`
- 午睡档：`SystemRequired` + `DisplayRequired`

`REASON_CONTEXT.SimpleReasonString` 必须在请求整个生命周期内有效，故 `Dispose` 时才 `FreeHGlobal`。回退线程用可中断的短睡眠循环，不用 `WaitHandle`，避免 Dispose 时释放事件导致后台线程未处理异常、整个进程退出。

## 快照与恢复

`%AppData%\CutScreen\snapshot.json` 只记录需要还原的持久化改动（显示器超时原值、DDC/CI 是否写过 Off）。

- 进程级资源（`PowerRequest` 句柄、黑窗口、DDC/CI 句柄）由 OS 在进程死亡时自动回收，不进快照
- 启动时 `Snapshot.Recover()` 先还原残留再正常工作
- 旧版本残留的 `MonitorTimeoutChanged=true` 快照仍兼容还原

## 持续集成

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml) 在 `windows-latest` 上跑 `dotnet restore` → `dotnet build -c Release` → 只读自检。每次 push 到 `main` 与 PR 触发。
