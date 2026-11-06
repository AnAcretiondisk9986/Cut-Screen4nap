namespace CutScreen;

/// <summary>
/// 检测「鼠标移动或键盘输入任意值」以退出档位。
///
/// 默认路径：GetLastInputInfo 轮询系统级最后输入时间戳 —— 零权限、零注入，
///   鼠标移动 / 任意按键都会改变它，正好对应「任意值」。
///
/// 可选（忽略鼠标抖动）：额外在专用 STA 消息循环线程装 WH_KEYBOARD_LL，
///   用来区分「键盘按下」和「光学鼠标微小抖动」。鼠标则用 GetCursorPos
///   位移判断，不需要鼠标钩子。钩子在 Start() 时安装，远早于 Arm()，无竞态。
/// </summary>
internal sealed class InputWatcher : IDisposable
{
    private readonly Action _onInput;
    private readonly bool _ignoreJitter;
    private readonly int _thresholdPx;
    private readonly int _thresholdSq;

    private System.Threading.Timer? _poll;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private Native.LowLevelProc? _keyProc;
    private IntPtr _keyHook = IntPtr.Zero;
    private volatile bool _hookReady;

    private volatile bool _armed;
    private uint _baseline;
    private Native.POINT _cursorStart;
    private volatile bool _keyboardFired;
    private int _inPoll;

    public InputWatcher(Action onInput, bool ignoreJitter, int thresholdPx)
    {
        _onInput = onInput;
        _ignoreJitter = ignoreJitter;
        _thresholdPx = Math.Max(1, thresholdPx);
        _thresholdSq = _thresholdPx * _thresholdPx;
    }

    public void Start()
    {
        if (_ignoreJitter) StartHookThread();
        _poll = new System.Threading.Timer(_ => Poll(), null,
            System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }

    /// <summary>正式进入档位、宽限期结束后调用：以此刻输入状态为基线。</summary>
    public void Arm()
    {
        Native.GetCursorPos(out _cursorStart);
        _keyboardFired = false;
        Volatile.Write(ref _baseline, Native.LastInputTick());
        _armed = true;
        _poll?.Change(150, 150);
    }

    public void Disarm()
    {
        _armed = false;
        _poll?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }

    private void Poll()
    {
        if (!_armed || Interlocked.Exchange(ref _inPoll, 1) != 0) return;
        try
        {
            uint now = Native.LastInputTick();
            uint base_ = Volatile.Read(ref _baseline);
            if (now == base_) return;
            // tick 回绕（约 49.7 天一次）：now 会突然小于基线，重设基线而不是误判为输入
            if (now < base_) { Volatile.Write(ref _baseline, now); return; }

            // 抖动过滤：钩子未就绪时自动退化为「任意输入即退出」，绝不吞掉真实输入
            if (_ignoreJitter && _hookReady && !_keyboardFired)
            {
                Native.GetCursorPos(out var p);
                int dx = p.X - _cursorStart.X, dy = p.Y - _cursorStart.Y;
                if (dx * dx + dy * dy < _thresholdSq)
                {
                    Volatile.Write(ref _baseline, now);   // 视为抖动，推进基线继续观察
                    return;
                }
            }

            _armed = false;
            _poll?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            try { _onInput(); } catch { }
        }
        finally { Interlocked.Exchange(ref _inPoll, 0); }
    }

    private void StartHookThread()
    {
        _hookThread = new Thread(() =>
        {
            _hookThreadId = Native.GetCurrentThreadId();
            _keyProc = (n, w, l) =>
            {
                if (n >= 0 && (w == (IntPtr)Native.WM_KEYDOWN || w == (IntPtr)Native.WM_SYSKEYDOWN))
                    _keyboardFired = true;
                return Native.CallNextHookEx(_keyHook, n, w, l);   // 必须透传原始 lParam
            };
            _keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _keyProc,
                Native.GetModuleHandle(null), 0);
            _hookReady = _keyHook != IntPtr.Zero;

            while (true)
            {
                int r = Native.GetMessage(out var m, IntPtr.Zero, 0, 0);
                if (r <= 0) break;
                Native.TranslateMessage(ref m);
                Native.DispatchMessage(ref m);
            }
            if (_keyHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_keyHook); _keyHook = IntPtr.Zero; }
        })
        { IsBackground = true, Name = "CutScreen.KeyHook" };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
    }

    public void Dispose()
    {
        _armed = false;
        _poll?.Dispose();
        _poll = null;
        if (_hookThreadId != 0)
            Native.PostThreadMessage(_hookThreadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
