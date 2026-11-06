using System.Runtime.InteropServices;

namespace CutScreen;

/// <summary>Win32 / DXVA2 / 电源管理 P-Invoke 集中层。所有可失败的调用都在上层做兜底。</summary>
internal static class Native
{
    // ---- 消息 / 显示器电源 ----
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint SC_MONITORPOWER = 0xF170;
    public const uint WM_WTSSESSION_CHANGE = 0x02B1;
    public const uint WTS_SESSION_LOCK = 0x7;
    public const uint WTS_SESSION_UNLOCK = 0x8;
    public const uint NOTIFY_FOR_THIS_SESSION = 0;

    public static readonly IntPtr HWND_BROADCAST = (IntPtr)0xFFFF;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    /// <summary>SC_MONITORPOWER 的 lParam：2=关，-1=开，1=低功耗。</summary>
    public static readonly IntPtr MONITOR_OFF = (IntPtr)2;
    public static readonly IntPtr MONITOR_ON = (IntPtr)(-1);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool LockWorkStation();

    // ---- 黑窗口按物理像素精确覆盖（绕过 WinForms DPI 虚拟化）----
    public static readonly IntPtr HWND_TOPMOST = (IntPtr)(-1);
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOSENDCHANGING = 0x0400;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public const int SW_HIDE = 0;

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSRegisterSessionNotification(IntPtr hWnd, uint dwFlags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    // ---- 空闲输入检测 ----
    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public static uint LastInputTick()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref lii) ? lii.dwTime : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern int ShowCursor(bool bShow);

    // ---- 低级钩子（仅「忽略鼠标抖动」可选功能启用时安装）----
    public const int WH_MOUSE_LL = 14;
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_SYSKEYDOWN = 0x0104;

    public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam;
        public uint time; public POINT pt; public uint lPrivate;
    }

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    public const uint WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

    // ---- 保活：PowerCreateRequest（句柄式，不绑线程）----
    public const uint POWER_REQUEST_CONTEXT_VERSION = 0;
    public const uint POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    public struct REASON_CONTEXT
    {
        public uint Version;
        public uint Flags;
        public IntPtr SimpleReasonString;
    }

    public enum PowerRequestType : int
    {
        PowerRequestDisplayRequired = 0,
        PowerRequestSystemRequired = 1,
        PowerRequestAwayModeRequired = 2,
        PowerRequestExecutionRequired = 3,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr PowerCreateRequest(ref REASON_CONTEXT Context);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool PowerSetRequest(IntPtr PowerRequest, PowerRequestType RequestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool PowerClearRequest(IntPtr PowerRequest, PowerRequestType RequestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    // 兜底方案：SetThreadExecutionState（per-thread，需要常驻线程）
    public const uint ES_CONTINUOUS = 0x80000000;
    public const uint ES_SYSTEM_REQUIRED = 0x00000001;
    public const uint ES_DISPLAY_REQUIRED = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint SetThreadExecutionState(uint esFlags);

    // ---- 电源计划读写（locale 无关，替代 powercfg 文本解析）----
    public static readonly Guid GUID_VIDEO_SUBGROUP = new("7516b95f-f776-4464-8c53-06167f40cc99");
    public static readonly Guid GUID_VIDEO_IDLE_TIMEOUT = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerReadACValueIndex(
        IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroup, ref Guid Setting, out uint AcValueIndex);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerWriteACValueIndex(
        IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroup, ref Guid Setting, uint AcValueIndex);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static bool TryGetActiveScheme(out Guid scheme)
    {
        scheme = Guid.Empty;
        if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0 || ptr == IntPtr.Zero) return false;
        try { scheme = Marshal.PtrToStructure<Guid>(ptr); return true; }
        finally { LocalFree(ptr); }
    }

    /// <summary>读取当前方案的「关闭显示器(AC)」秒数。</summary>
    public static bool TryGetMonitorTimeout(out uint seconds)
    {
        seconds = 0;
        if (!TryGetActiveScheme(out var scheme)) return false;
        var sub = GUID_VIDEO_SUBGROUP;
        var set = GUID_VIDEO_IDLE_TIMEOUT;
        return PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, out seconds) == 0;
    }

    /// <summary>写入并刷新当前方案。调用方负责在退出时还原原值。</summary>
    public static bool TrySetMonitorTimeout(uint seconds)
    {
        if (!TryGetActiveScheme(out var scheme)) return false;
        var sub = GUID_VIDEO_SUBGROUP;
        var set = GUID_VIDEO_IDLE_TIMEOUT;
        if (PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref set, seconds) != 0) return false;
        // 重新应用方案以让改动立即生效
        return PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;
    }

    // ---- DDC/CI（可选高级策略）----
    public const byte VCP_POWER = 0xD6;
    public const uint VCP_ON = 0x01;   // DPM: On
    public const uint VCP_OFF = 0x04;  // DPM: Off（VESA）。不用 0x05：部分型号只能物理键唤醒

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    public delegate bool EnumMonitorsDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsDelegate lpfnEnum, IntPtr dwData);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool GetPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor, uint arraySize, [Out] PHYSICAL_MONITOR[] array);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool SetVCPFeature(IntPtr hMonitor, byte bVCPCode, uint dwNewValue);

    [DllImport("dxva2.dll", SetLastError = true)]
    public static extern bool DestroyPhysicalMonitors(uint arraySize, [In] PHYSICAL_MONITOR[] array);
}
