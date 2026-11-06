using System.Runtime.InteropServices;

namespace CutScreen;

/// <summary>
/// 保活。默认只挂 SystemRequired（系统不睡、允许屏幕随便灭）。
///
/// keepDisplayAwake=true 时额外挂 DisplayRequired：用于「午睡档」——
/// 黑窗口盖住屏幕期间，必须阻止系统因空闲把显示器送进 DPMS 待机，
/// 否则外接屏会进深睡、退出时链路重训练造成闪烁/断连。
/// 它是进程级句柄、不绑线程、不改任何全局电源设置、进程退出自动释放。
///
/// 首选 PowerCreateRequest（句柄式、powercfg /requests 可见）；
/// 失败时回退到 SetThreadExecutionState（per-thread，必须跑在常驻线程上）。
/// </summary>
internal sealed class WakeLock : IDisposable
{
    private readonly bool _keepDisplayAwake;
    private IntPtr _request = IntPtr.Zero;
    private IntPtr _reasonPtr = IntPtr.Zero;   // 必须在请求存活期间保持有效
    private Thread? _fallbackThread;
    private volatile bool _running;
    private volatile bool _usingFallback;

    public bool UsingPowerRequest => _request != IntPtr.Zero && !_usingFallback;

    public WakeLock(bool keepDisplayAwake = false) => _keepDisplayAwake = keepDisplayAwake;

    public void Acquire()
    {
        // --- 首选：PowerCreateRequest ---
        try
        {
            // 文档要求 SimpleReasonString 在请求整个生命周期内有效，故 Dispose 时才释放
            _reasonPtr = Marshal.StringToHGlobalUni(
                _keepDisplayAwake
                    ? "Cut-Screen 午睡档：保持系统与显示器供电（避免外接屏待机重枚举）"
                    : "Cut-Screen 正在保持系统运行（夜间任务）");
            var ctx = new Native.REASON_CONTEXT
            {
                Version = Native.POWER_REQUEST_CONTEXT_VERSION,
                Flags = Native.POWER_REQUEST_CONTEXT_SIMPLE_STRING,
                SimpleReasonString = _reasonPtr
            };
            _request = Native.PowerCreateRequest(ref ctx);
            if (_request != IntPtr.Zero && _request != (IntPtr)(-1))
            {
                bool okSys = Native.PowerSetRequest(_request, Native.PowerRequestType.PowerRequestSystemRequired);
                bool okDisp = !_keepDisplayAwake ||
                    Native.PowerSetRequest(_request, Native.PowerRequestType.PowerRequestDisplayRequired);
                if (okSys && okDisp) return;
            }
            ReleaseRequest();
        }
        catch { ReleaseRequest(); }

        // --- 回退：常驻线程持续刷新 SetThreadExecutionState ---
        // 不用 WaitHandle：Dispose 时若线程正阻塞在 Wait 上，释放事件会抛 ObjectDisposedException
        // 进而导致后台线程未处理异常、整个进程退出。这里用可中断的短睡眠循环。
        _usingFallback = true;
        _running = true;
        uint flags = Native.ES_CONTINUOUS | Native.ES_SYSTEM_REQUIRED |
                     (_keepDisplayAwake ? Native.ES_DISPLAY_REQUIRED : 0);
        _fallbackThread = new Thread(() =>
        {
            while (_running)
            {
                Native.SetThreadExecutionState(flags);
                for (int i = 0; i < 30 && _running; i++) Thread.Sleep(1000);
            }
            Native.SetThreadExecutionState(Native.ES_CONTINUOUS);
        })
        { IsBackground = true, Name = "CutScreen.WakeLock" };
        _fallbackThread.Start();
    }

    private void ReleaseRequest()
    {
        if (_request != IntPtr.Zero && _request != (IntPtr)(-1))
        {
            try { Native.PowerClearRequest(_request, Native.PowerRequestType.PowerRequestSystemRequired); }
            catch { }
            if (_keepDisplayAwake)
            {
                try { Native.PowerClearRequest(_request, Native.PowerRequestType.PowerRequestDisplayRequired); }
                catch { }
            }
            try { Native.CloseHandle(_request); } catch { }
        }
        _request = IntPtr.Zero;
        if (_reasonPtr != IntPtr.Zero)
        {
            try { Marshal.FreeHGlobal(_reasonPtr); } catch { }
            _reasonPtr = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        ReleaseRequest();
        _running = false;   // 不 Join：background 线程，进程退出时自然结束，避免卡住退出流程
    }
}
