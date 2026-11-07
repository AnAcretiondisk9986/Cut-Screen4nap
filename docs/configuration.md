# 配置速查

## 配置文件

设置保存在 `%AppData%\CutScreen\config.json`，由设置页自动写入。不存在时使用内置默认值。

| 字段 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `EnterDelaySeconds` | int | `20` | 选档后进入前的可取消延时，够你放好键鼠躺下 |
| `ExitGraceSeconds` | int | `3` | 进入后忽略输入的宽限期，吃掉放键盘那最后一下 |
| `RememberLastMode` | bool | `true` | 记住上次选择的档位 |
| `LastMode` | enum | `Nap` | 上次档位：`Hibernate` / `StayAwake` / `Nap` |
| `StayAwakeLockScreen` | bool | `false` | 挂机档是否锁屏；开启后任意输入只点亮锁屏，仍需输 PIN |
| `UseDdcCi` | bool | `false` | 挂机档外接屏是否优先用 DDC/CI 关背光，需先跑校准 |
| `IgnoreMouseJitter` | bool | `false` | 忽略低于阈值的鼠标微小位移 |
| `MouseThresholdPx` | int | `4` | 抖动过滤的位移阈值（像素） |
| `RunAtStartup` | bool | `false` | 开机自启（静默）。以注册表运行项为准，启动时自动对齐 |

示例：

```json
{
  "EnterDelaySeconds": 20,
  "ExitGraceSeconds": 3,
  "RememberLastMode": true,
  "LastMode": "Nap",
  "StayAwakeLockScreen": false,
  "UseDdcCi": false,
  "IgnoreMouseJitter": false,
  "MouseThresholdPx": 4,
  "RunAtStartup": false
}
```

## 快照文件

`%AppData%\CutScreen\snapshot.json` 只在挂机 / 午睡档运行期间存在，退出时自动删除。进程被强杀或断电后，下次启动会先还原再工作。

| 字段 | 说明 |
|---|---|
| `Mode` | 进入时的档位 |
| `EnteredAtUtc` | 进入时间（UTC） |
| `MonitorTimeoutChanged` | 兼容旧版本；当前版本恒为 `false` |
| `MonitorTimeoutOriginalSeconds` | 旧版本记录的显示器超时原值 |
| `DdcCiWrittenOff` | 是否向显示器写过 `VCP 0xD6 = Off` |

不要把 `snapshot.json` 手动提交到仓库，它包含本机电源状态。

## 命令

构建：

```bash
dotnet build -c Release
```

只读自检：

```powershell
dotnet run -c Release -p:DefineConstants=SELFTEST -p:OutputType=Exe
```

验收保活：

```powershell
powercfg /requests
```

启用休眠（一次性，需管理员）：

```powershell
powercfg /hibernate on
```

查看可用睡眠状态：

```powershell
powercfg /a
```

## 开机自启（静默）

推荐在 **设置 → 开机自启** 勾选「开机自动启动（静默，不弹主窗口）」并点「保存并关闭」。程序会写当前用户的注册表运行项：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
  CutScreen = "…\CutScreen.exe" --silent
```

- 无需管理员、无需计划任务；只影响当前用户。
- 登录时以 `--silent` 启动：托盘就绪，主窗口不弹出、不抢焦点、不弹气泡。
- 命令行可单独验证：`CutScreen.exe --silent`（也接受 `/silent`）。
- 启动时 `StartupManager.Sync` 会把配置 `RunAtStartup` 与注册表对齐：exe 搬家会自动更新命令行路径，运行项被手动删除则回写配置为关。
- 取消勾选会移除该运行项。

手动方式（不推荐）：`Win+R` → `shell:startup` → 放 `CutScreen.exe` 的快捷方式，并在快捷方式目标后加 `--silent`。必须保证快捷方式指向的 exe 与 `CutScreen.dll`、`CutScreen.runtimeconfig.json`、`CutScreen.deps.json` 在同一目录。
