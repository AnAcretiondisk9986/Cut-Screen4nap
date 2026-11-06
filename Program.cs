using System.Windows.Forms;

namespace CutScreen;

internal static class Program
{
    private const string MutexName = @"Global\CutScreen_SingleInstance_6f3a1c";

    [STAThread]
    private static void Main()
    {
#if SELFTEST
        if (NativeMethods.AllocConsole()) { /* 附加到调用方的控制台 */ }
        SelfTest.Run();
#else
        using var mutex = new Mutex(true, MutexName, out bool created);
        if (!created)
        {
            // 已有实例在跑：唤醒它的主窗口
            NativeMethods.PostMessage((IntPtr)0xFFFF,
                unchecked((uint)TrayAppContext.WM_SHOW_FIRST_INSTANCE), IntPtr.Zero, IntPtr.Zero);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetCompatibleTextRenderingDefault(false);

        var cfg = Config.Load();
        var ctx = new TrayAppContext(cfg);
        Application.Run(ctx);
        GC.KeepAlive(mutex);
#endif
    }
}

internal static class NativeMethods
{
#if SELFTEST
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    public static extern bool AllocConsole();
#endif

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern uint RegisterWindowMessage(string lpString);
}
