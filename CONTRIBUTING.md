# 贡献

> 本项目全程由 **Tencent Hy-4** 与 **ZCode** 开发。

## 环境

- Windows 10/11 x64
- .NET 10 SDK ≥ 10.0.100

## 最短流程

```powershell
git clone https://github.com/AnAcretiondisk9986/Cut-Screen4nap.git
cd Cut-Screen4nap
dotnet build -c Release
dotnet run -c Release -p:DefineConstants=SELFTEST -p:OutputType=Exe
```

fork → 建分支 → 修改 → 上面两条命令都通过 → 提 PR。提交标题用 Conventional Commit（如 `fix: 修正午睡档退出时的显示器断连`）。

## 改动前须知

- 会关屏 / 锁屏 / 休眠的代码路径无法在 CI 里实测，改完请在真机上按 [docs/guide.md](docs/guide.md) 的「首次使用建议」验证。
- 凡是新增「修改持久化系统设置」的逻辑，必须同时实现快照落盘与崩溃恢复，参见 [docs/architecture.md](docs/architecture.md)。
- 界面改动必须避免硬编码 `Width` / `Height`（高 DPI 下会裁切文字），用 `AutoSize` 或固定行高的 `TableLayoutPanel`。
- 保活请求只挂 `PowerRequestSystemRequired`，挂机档不得挂 `PowerRequestDisplayRequired`；午睡档挂它是为了阻止系统自己待机，不要互换。

## 文档

- README 只放索引与最短路径，深度内容写在 `docs/`。
- 修改 [docs/configuration.md](docs/configuration.md) 的字段表时，同步核对 `Config.cs`。
