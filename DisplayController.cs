using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CutScreen;

/// <summary>负责「黑屏」的三种实现：纯黑窗口 / DPMS 广播 / DDC-CI 关背光。</summary>
internal sealed class DisplayController : IDisposable
{
    private readonly List<BlackForm> _windows = new();
    private readonly List<Native.PHYSICAL_MONITOR> _ddcMonitors = new();
    private int _cursorHidden;

#if SELFTEST
    internal static List<MonitorInfo> EnumerateMonitorsForTest() => EnumerateMonitors();
    internal static int ProbePhysicalMonitorsForTest() => CollectPhysicalMonitors().Sum(c => c.Physical.Length);
#endif

    // ---------- 显示器枚举 ----------
    public sealed record MonitorInfo(IntPtr Handle, Rectangle Bounds, bool IsPrimary, string DeviceName);

    private static List<MonitorInfo> EnumerateMonitors()
    {
        var list = new List<MonitorInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, _, ref rc, _) =>
        {
            var r = new Rectangle(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
            // 用 System.Windows.Forms.Screen 补出主屏标记和设备名
            var scr = Screen.AllScreens.FirstOrDefault(s => s.Bounds == r);
            list.Add(new MonitorInfo(h, r, scr?.Primary ?? false, scr?.DeviceName ?? ""));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    // ---------- 纯黑窗口（午睡档）----------
    private sealed class BlackForm : Form
    {
        public BlackForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.Black;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Text = "";
            Cursor = Cursors.Default;
            // 不抢焦点、不进 Alt+Tab、不响应鼠标穿透
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x80;
                const int WS_EX_NOACTIVATE = 0x08000000;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override void WndProc(ref Message m)
        {
            // 吞掉鼠标左键，避免误点穿透到桌面
            const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONDOWN = 0x0204;
            if (m.Msg is WM_LBUTTONDOWN or WM_LBUTTONDBLCLK or WM_RBUTTONDOWN) return;
            base.WndProc(ref m);
        }

        public void Place(Rectangle phys)
        {
            // 用 SetWindowPos 按物理像素定位，绕过 WinForms 的 DPI 虚拟化
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST,
                phys.X, phys.Y, phys.Width, phys.Height,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_NOSENDCHANGING);
        }
    }

    public string ShowBlackWindows()
    {
        // 必须在 UI 线程调用：Form 依赖消息循环处理 WM_PAINT，否则窗口会显示为白/空白。
        var mons = EnumerateMonitors();
        foreach (var m in mons) CreateBlackWindow(m.Bounds);
        HideCursorOnce();
        return $"已在 {mons.Count} 块屏幕上覆盖纯黑窗口（IPS 会透出深灰）";
    }

    private void CreateBlackWindow(Rectangle phys)
    {
        var f = new BlackForm();
        _windows.Add(f);
        f.Bounds = phys;   // 先给 WinForms 内部状态一个正确位置，避免 Show 时闪在 (0,0)
        f.Show();
        f.Place(phys);     // 再用物理像素校正，绕开任何 DPI 虚拟化
    }

    private void HideCursorOnce()
    {
        // ShowCursor(false) 把显示计数减一；一次即可，退出时对称加一。
        // 绝不能用「循环直到返回负数」——那样会把计数压到 -N，退出后光标永久消失。
        if (_cursorHidden == 0)
        {
            Native.ShowCursor(false);
            _cursorHidden = 1;
        }
    }

    /// <summary>
    /// 每 30 秒重暗一次：重新枚举当前拓扑，按「现在有几块屏」补齐/移除黑窗口。
    /// 运行中插拔显示器、或合盖/开盖导致拓扑变化，都能跟上。
    /// 必须在 UI 线程调用（会创建/销毁 Form）。
    /// </summary>
    public void ReassertBlackWindows()
    {
        var mons = EnumerateMonitors();

        // 运行中新增的显示器：补建窗口，否则那块屏会一直亮着
        while (_windows.Count < mons.Count)
            CreateBlackWindow(mons[_windows.Count].Bounds);

        // 运行中拔掉的显示器：关掉多余窗口
        while (_windows.Count > mons.Count)
        {
            int last = _windows.Count - 1;
            try { _windows[last].Close(); _windows[last].Dispose(); } catch { }
            _windows.RemoveAt(last);
        }

        // 窗口位置/尺寸可能因分辨率或 DPI 变化而改变，逐块重新校正
        for (int i = 0; i < _windows.Count; i++)
        {
            var r = mons[i].Bounds;
            Native.SetWindowPos(_windows[i].Handle, Native.HWND_TOPMOST,
                r.X, r.Y, r.Width, r.Height,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_NOSENDCHANGING);
        }
        HideCursorOnce();   // 新会话里若计数被系统改过，保证仍是隐藏状态
    }

    private void RemoveBlackWindows()
    {
        // 先把所有窗口一次性隐藏，再逐个 Dispose。
        // 若直接 Close() 循环，多块屏会「依次」露出桌面，视觉上就是一阵闪烁。
        foreach (var f in _windows)
        {
            try { Native.ShowWindow(f.Handle, Native.SW_HIDE); } catch { }
        }
        foreach (var f in _windows)
        {
            try { f.Close(); f.Dispose(); } catch { }
        }
        _windows.Clear();
        if (_cursorHidden > 0) { Native.ShowCursor(true); _cursorHidden = 0; }
    }

    // ---------- DPMS 广播（内屏 / 回退）----------
    public static void DpmsOff()
    {
        // SMTO_ABORTIFHUNG：任何顶层窗口卡死都不能把本进程挂住
        Native.SendMessageTimeout(Native.HWND_BROADCAST, Native.WM_SYSCOMMAND,
            (IntPtr)Native.SC_MONITORPOWER, Native.MONITOR_OFF,
            Native.SMTO_ABORTIFHUNG, 3000, out _);
    }

    public static void DpmsOn()
    {
        Native.SendMessageTimeout(Native.HWND_BROADCAST, Native.WM_SYSCOMMAND,
            (IntPtr)Native.SC_MONITORPOWER, Native.MONITOR_ON,
            Native.SMTO_ABORTIFHUNG, 3000, out _);
    }

    // ---------- DDC/CI ----------
    private struct Collected
    {
        public IntPtr HMonitor;
        public Native.PHYSICAL_MONITOR[] Physical;
    }

    private static List<Collected> CollectPhysicalMonitors()
    {
        var result = new List<Collected>();
        foreach (var m in EnumerateMonitors())
        {
            try
            {
                if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(m.Handle, out uint count) || count == 0)
                    continue;
                var arr = new Native.PHYSICAL_MONITOR[count];
                if (Native.GetPhysicalMonitorsFromHMONITOR(m.Handle, count, arr))
                    result.Add(new Collected { HMonitor = m.Handle, Physical = arr });
            }
            catch (DllNotFoundException) { return result; }
            catch (EntryPointNotFoundException) { return result; }
            catch { }
        }
        return result;
    }

    /// <summary>返回 (成功关背光台数, 说明文字)。失败的物理句柄立即释放。</summary>
    public (int Ok, string Report) DdcOff()
    {
        int ok = 0, total = 0;
        foreach (var c in CollectPhysicalMonitors())
        {
            foreach (var pm in c.Physical)
            {
                total++;
                // 内屏 eDP 通常不响应；失败就交给 DPMS 兜底，不报错
                if (TryVcp(pm.hPhysicalMonitor, Native.VCP_OFF))
                {
                    _ddcMonitors.Add(pm);   // 句柄留给退出时写回 0x01
                    ok++;
                }
                else
                {
                    try { Native.CloseHandle(pm.hPhysicalMonitor); } catch { }
                }
            }
        }
        string report = total == 0
            ? "未检测到支持 DDC/CI 的物理显示器，回退 DPMS"
            : $"DDC/CI 关背光：{ok}/{total} 台成功（其余由 DPMS 兜底）";
        return (ok, report);
    }

    private static bool TryVcp(IntPtr h, uint value)
    {
        try { return Native.SetVCPFeature(h, Native.VCP_POWER, value); }
        catch { return false; }
    }

    public bool HasDdcCiMonitors => _ddcMonitors.Count > 0;

    /// <summary>退出/崩溃恢复时把 DDC/CI 写回 On，然后释放句柄。</summary>
    public int DdcRestore()
    {
        int n = 0;
        foreach (var pm in _ddcMonitors)
        {
            if (TryVcp(pm.hPhysicalMonitor, Native.VCP_ON)) n++;
            try { Native.CloseHandle(pm.hPhysicalMonitor); } catch { }
        }
        _ddcMonitors.Clear();
        return n;
    }

    /// <summary>静态兜底：崩溃后没有句柄列表，重新枚举并尽力写回 0x01。</summary>
    public static int TryRestoreAllVcp()
    {
        int n = 0;
        foreach (var c in CollectPhysicalMonitors())
        {
            foreach (var pm in c.Physical)
            {
                try
                {
                    if (Native.SetVCPFeature(pm.hPhysicalMonitor, Native.VCP_POWER, Native.VCP_ON)) n++;
                    Native.CloseHandle(pm.hPhysicalMonitor);
                }
                catch { }
            }
        }
        return n;
    }

    /// <summary>设置页的「一键校准」：确认写 Off 后能写回 On。</summary>
    public static string Calibrate()
    {
        var ms = CollectPhysicalMonitors();
        if (ms.Count == 0) return "未检测到支持 DDC/CI 的显示器，夜间档会自动走 DPMS。";
        int pass = 0, total = 0;
        foreach (var c in ms)
        {
            foreach (var pm in c.Physical)
            {
                total++;
                bool off = TryVcp(pm.hPhysicalMonitor, Native.VCP_OFF);
                System.Threading.Thread.Sleep(250);
                bool on = TryVcp(pm.hPhysicalMonitor, Native.VCP_ON);
                if (off && on) pass++;
                try { Native.CloseHandle(pm.hPhysicalMonitor); } catch { }
            }
        }
        return $"校准：{pass}/{total} 台可安全「关背光→写回」。\n"
             + (pass == total
                ? "全部通过，夜间档将使用 DDC/CI（不会触发显示器重枚举）。"
                : "有显示器写不回，已自动回退 DPMS；建议在这些显示器 OSD 里关闭 Deep Sleep。");
    }

    public void Dispose() => RemoveBlackWindows();
}
