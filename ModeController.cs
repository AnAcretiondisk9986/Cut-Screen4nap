using System.Diagnostics;
using Microsoft.Win32;

namespace CutScreen;

internal enum Phase { Idle, Countdown, Entering, Active }

internal sealed class ModeState
{
    public Phase Phase { get; set; } = Phase.Idle;
    public Mode? Mode { get; set; }
    public DateTime EnteredAt { get; set; }
    public string Detail { get; set; } = "";

    public string Label => Phase switch
    {
        Phase.Countdown => $"即将进入【{ModeNames.Cn(Mode!.Value)}】",
        Phase.Entering => $"正在进入【{ModeNames.Cn(Mode!.Value)}】",
        Phase.Active => $"【{ModeNames.Cn(Mode!.Value)}】运行中",
        _ => "就绪"
    };
}

/// <summary>
/// 三档共用的状态机。A 一次性动作（无状态），B/C 为 enter/exit 会话（有快照）。
///
/// 线程模型：所有状态迁移都经 _ui 封送到 UI 线程执行。计时器回调、输入监视器
/// 回调都先 _ui(...) 再干活 —— 因为黑窗口是 WinForms Form，跨线程创建/销毁会白屏。
/// 唯一例外是 InputWatcher.Arm / GetLastInputInfo 这类纯 Win32 调用（线程安全）。
/// </summary>
internal sealed class ModeController : IDisposable
{
    private readonly Config _cfg;
    private readonly Action _refreshUi;
    private readonly Action<string> _notify;
    private readonly Action<Action> _ui;   // 把操作封送回 UI 线程（BeginInvoke）
    private readonly object _gate = new();
    private ModeState _state = new();
    private System.Threading.Timer? _countdown;
    private System.Threading.Timer? _grace;
    private System.Threading.Timer? _reassert;
    private WakeLock? _wakeLock;
    private DisplayController? _display;
    private InputWatcher? _watcher;
    private Snapshot? _snapshot;
    private long _countdownEndTicks;
    private int _generation;   // 每次进入/退出递增，作废已排队但尚未执行的重暗动作

    public ModeState State { get { lock (_gate) return _state; } }
    public bool IsBusy => State.Phase is Phase.Countdown or Phase.Entering or Phase.Active;
    public DateTime CountdownEnd => new(Interlocked.Read(ref _countdownEndTicks), DateTimeKind.Local);

    public ModeController(Config cfg, Action refreshUi, Action<string> notify, Action<Action> ui)
    {
        _cfg = cfg;
        _refreshUi = refreshUi;
        _notify = notify;
        _ui = ui;
    }

    // ---------- 对外操作（可从任意线程调用）----------

    public void Request(Mode mode)
    {
        lock (_gate)
        {
            if (_state.Phase != Phase.Idle)
            {
                _notify("已有档位在进行中，请先取消或退出。");
                return;
            }
            _state = new ModeState { Phase = Phase.Countdown, Mode = mode };
            Interlocked.Exchange(ref _countdownEndTicks,
                DateTime.Now.AddSeconds(Math.Max(0, _cfg.EnterDelaySeconds)).Ticks);
        }

        if (_cfg.RememberLastMode) { _cfg.LastMode = mode; _cfg.Save(); }

        int ms = Math.Max(0, _cfg.EnterDelaySeconds) * 1000;
        // 倒计时回调来自线程池 → 封送回 UI 线程
        _countdown?.Dispose();
        _countdown = new System.Threading.Timer(_ => _ui(() => FinishCountdown(mode)), null, ms,
            System.Threading.Timeout.Infinite);
        _refreshUi();
    }

    public void Cancel() => _ui(CancelCore);

    /// <summary>任意输入到达 / 托盘退出 / 手动退出，统一走这里。</summary>
    public void Exit(bool showNotification = true) => _ui(() => ExitCore(showNotification));

    /// <summary>仅由 TrayAppContext 在 UI 线程、真正退出程序时调用。</summary>
    public void Shutdown()
    {
        StopTimers();
        _watcher?.Disarm();
        TeardownResources(State.Mode);
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>设置页改了抖动过滤开关后重建 watcher（UI 线程调用）。</summary>
    public void RebuildWatcher()
    {
        _watcher?.Dispose();
        _watcher = new InputWatcher(() => Exit(), _cfg.IgnoreMouseJitter, _cfg.MouseThresholdPx);
        _watcher.Start();
    }

    // ---------- 内部流程（全部运行在 UI 线程）----------

    private void StopTimers()
    {
        Interlocked.Increment(ref _generation);   // 作废已排队但未执行的重暗动作
        _countdown?.Dispose(); _countdown = null;
        _grace?.Dispose(); _grace = null;
        _reassert?.Dispose(); _reassert = null;
    }

    private void CancelCore()
    {
        StopTimers();
        _watcher?.Disarm();
        Mode? mode;
        lock (_gate) { mode = _state.Mode; _state = new ModeState(); }
        TeardownResources(mode);
        _refreshUi();
        _notify("已取消。");
    }

    private void ExitCore(bool showNotification)
    {
        bool wasActive;
        Mode? mode;
        lock (_gate)
        {
            wasActive = _state.Phase != Phase.Idle;
            mode = _state.Mode;
            StopTimers();
            _watcher?.Disarm();
            _state = new ModeState();
        }
        if (!wasActive) return;

        TeardownResources(mode);

        if (showNotification)
        {
            _notify(mode == Mode.Hibernate
                ? "已退出休眠流程（休眠为一次性动作，无需还原）。"
                : $"已退出【{ModeNames.Cn(mode!.Value)}】，设置已还原。");
        }
        _refreshUi();
    }

    private void FinishCountdown(Mode mode)
    {
        lock (_gate)
        {
            if (_state.Phase != Phase.Countdown || _state.Mode != mode) return;
            _state.Phase = Phase.Entering;
        }
        _countdown?.Dispose(); _countdown = null;
        _refreshUi();

        try
        {
            switch (mode)
            {
                case Mode.Hibernate: EnterHibernate(); break;
                case Mode.StayAwake: EnterStayAwake(); break;
                case Mode.Nap: EnterNap(); break;
            }
        }
        catch (Exception ex)
        {
            _notify("进入失败：" + ex.Message);
            ExitCore(showNotification: false);
        }
    }

    // ----- A 档：休眠（一次性，零状态）-----
    private void EnterHibernate()
    {
        if (!HibernateEnabled())
        {
            var r = System.Windows.Forms.MessageBox.Show(
                "本机尚未启用休眠（hibernate）。\n\n需要以管理员身份执行一次 powercfg /hibernate on。\n现在尝试自动启用吗？",
                "Cut-Screen", System.Windows.Forms.MessageBoxButtons.YesNo,
                System.Windows.Forms.MessageBoxIcon.Warning);
            if (r != System.Windows.Forms.DialogResult.Yes) { ExitCore(false); return; }
            if (!TryEnableHibernate())
            {
                System.Windows.Forms.MessageBox.Show(
                    "自动启用失败。请手动以管理员身份运行：\npowercfg /hibernate on",
                    "Cut-Screen", System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                ExitCore(false); return;
            }
        }

        _notify("正在休眠… 唤醒请按电源键。");
        // 一次性动作：不建快照、不保活、不留状态
        Process.Start(new ProcessStartInfo("shutdown", "/h")
        {
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
        // 内存状态写盘时一并冻结；唤醒后 _state 就是 Idle，程序随镜像复活
        lock (_gate) { _state = new ModeState(); }
        _refreshUi();
    }

    private static bool HibernateEnabled()
    {
        // 不用 powercfg /a 的文本解析：它的「尚未启用」段落同样含「休眠」字样，会误判。
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            return k?.GetValue("HibernateEnabled") is int v && v == 1;
        }
        catch { return false; }
    }

    private static bool TryEnableHibernate()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo("powercfg", "/hibernate on")
            { UseShellExecute = true, Verb = "runas", CreateNoWindow = true });
            p?.WaitForExit(10000);
            return HibernateEnabled();
        }
        catch { return false; }
    }

    // ----- B 档：挂机（有任务，彻底黑，可选锁屏）-----
    private void EnterStayAwake()
    {
        _display = new DisplayController();
        _snapshot = new Snapshot { Mode = Mode.StayAwake, EnteredAtUtc = DateTime.UtcNow };

        // 1. 保活（只挂 SYSTEM，绝不挂 DISPLAY）
        _wakeLock = new WakeLock();
        _wakeLock.Acquire();

        // 2. 锁屏为可选项，默认关：保证「任意输入即退出」不被 PIN 打断
        if (_cfg.StayAwakeLockScreen)
        {
            Native.LockWorkStation();
            System.Threading.Thread.Sleep(400); // 等 Winlogon 切换桌面
        }

        // 3. 显示层：外接 DDC/CI → 失败回退 DPMS；内屏始终 DPMS
        int ddcOk = 0;
        string displayReport;
        if (_cfg.UseDdcCi) (ddcOk, displayReport) = _display.DdcOff();
        else displayReport = "已按设置跳过 DDC/CI";

        _snapshot.DdcCiWrittenOff = ddcOk > 0;
        DisplayController.DpmsOff();   // 内屏 + 所有未被 DDC/CI 关掉的屏

        // 4. 落盘快照（进程崩溃时下次启动还原）
        Snapshot.Save(_snapshot);

        string lockNote = _cfg.StayAwakeLockScreen ? "（已锁屏）" : "";
        EnterActive(Mode.StayAwake,
            $"{displayReport}；{(_wakeLock!.UsingPowerRequest ? "PowerRequest" : "SES 回退")} 保活{lockNote}");

        // 持续低频重暗，压住播放器/下载工具的空闲计时刷新
        StartReassert(() => DisplayController.DpmsOff());
    }

    // ----- C 档：午睡（软暗，秒回）-----
    private void EnterNap()
    {
        _display = new DisplayController();
        _snapshot = new Snapshot { Mode = Mode.Nap, EnteredAtUtc = DateTime.UtcNow };

        // 午睡档：系统不睡 + 显示器不许进 DPMS 待机。
        // 用 DisplayRequired 句柄（进程级、不改任何全局设置）而不是改「关闭显示器」超时：
        // 一旦显示器待机，外接屏退出时链路重训练 → 闪烁/断连，正是这个方案要躲的坑。
        // 黑窗口是唯一遮光层，所以屏幕会透出 IPS 深灰（午睡档可接受）。
        _wakeLock = new WakeLock(keepDisplayAwake: true);
        _wakeLock.Acquire();

        var rep = _display.ShowBlackWindows();   // 创建 Form —— 必须在 UI 线程
        Snapshot.Save(_snapshot);

        EnterActive(Mode.Nap, rep);

        // 持续重新置顶黑窗口、并跟随显示器热插拔补齐/移除
        StartReassert(() => _display!.ReassertBlackWindows());
    }

    private void EnterActive(Mode mode, string detail)
    {
        lock (_gate)
        {
            _state = new ModeState
            {
                Phase = Phase.Active,
                Mode = mode,
                EnteredAt = DateTime.Now,
                Detail = detail
            };
        }
        _refreshUi();
        _notify($"已进入【{ModeNames.Cn(mode)}】。{detail}\n移动鼠标或按任意键即可退出。");

        // 宽限期：吃掉「放置键鼠 / 躺下」的最后几次输入，之后才取基线
        _watcher ??= CreateWatcher();
        int grace = Math.Max(0, _cfg.ExitGraceSeconds) * 1000;
        _grace?.Dispose();
        _grace = new System.Threading.Timer(_ =>
        {
            lock (_gate) { if (_state.Phase != Phase.Active) return; }
            _watcher?.Arm();
            _grace?.Dispose(); _grace = null;
        }, null, grace, System.Threading.Timeout.Infinite);
    }

    private InputWatcher CreateWatcher()
    {
        var w = new InputWatcher(() => Exit(), _cfg.IgnoreMouseJitter, _cfg.MouseThresholdPx);
        w.Start();
        return w;
    }

    /// <summary>
    /// 整个档位期间持续重暗，间隔 60 秒。由 Exit/Cancel 的 generation 守卫负责停。
    /// 兼任三件事：压住播放器/下载工具的空闲计时刷新、压住置顶通知盖到黑窗口上、
    /// 以及运行中插拔显示器时补齐/移除黑窗口。
    /// </summary>
    private void StartReassert(Action action)
    {
        int gen = Volatile.Read(ref _generation);
        _reassert?.Dispose();
        _reassert = new System.Threading.Timer(_ =>
        {
            // 退出后已排队的动作必须作废，否则会把刚唤醒的屏幕又关掉
            if (Volatile.Read(ref _generation) != gen) return;
            lock (_gate) { if (_state.Phase != Phase.Active) return; }
            // 动作可能碰 Form（午睡档置顶），封送回 UI 线程；执行时再校验一次
            _ui(() =>
            {
                if (Volatile.Read(ref _generation) != gen) return;
                lock (_gate) { if (_state.Phase != Phase.Active) return; }
                try { action(); } catch { }
            });
        }, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// 反序还原（UI 线程）。按档位区分：
    /// - 午睡档：显示器全程供电，绝不发 DpmsOn、不改电源计划 —— 只需撤黑窗口 + 释放保活。
    ///   发 MONITOR_ON 会让外接屏做一次 power cycle（链路重训练 → 闪烁/断连），是纯多余的。
    /// - 挂机档：发过 MONITOR_OFF / 改过电源超时 / 可能写过 DDC-CI，需完整还原。
    /// </summary>
    private void TeardownResources(Mode? mode)
    {
        try
        {
            bool isNap = mode == Mode.Nap;

            if (_display is not null)
            {
                if (!isNap)
                {
                    // 先用 DPMS 唤醒视频链路，显示器退出待机后才可能响应 DDC/CI
                    DisplayController.DpmsOn();
                    if (_display.HasDdcCiMonitors)
                    {
                        System.Threading.Thread.Sleep(150);
                        _display.DdcRestore();
                    }
                }
                _display.Dispose();   // 撤黑窗口（内部先统一隐藏再 Dispose，避免逐块露出闪烁）
                _display = null;
            }
            else if (!isNap)
            {
                DisplayController.DpmsOn();
            }

            // 兼容旧版本残留的快照：早期版本会改显示器超时，需还原。
            // 当前版本两档都不碰电源计划，正常运行下此分支恒不触发。
            if (_snapshot?.MonitorTimeoutChanged == true)
                Native.TrySetMonitorTimeout(_snapshot.MonitorTimeoutOriginalSeconds);

            // 释放保活：DisplayRequired 句柄随这里一起释放，显示器超时立即回到用户原值
            _wakeLock?.Dispose();
            _wakeLock = null;

            if (_snapshot is not null) { Snapshot.Clear(); _snapshot = null; }
        }
        catch (Exception ex)
        {
            _notify("还原时出现问题：" + ex.Message);
        }
    }

    public void Dispose() => Shutdown();
}
