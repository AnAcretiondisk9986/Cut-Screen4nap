using System.Windows.Forms;

namespace CutScreen;

internal static class Program
{
    private const string MutexName = @"Global\CutScreen_SingleInstance_6f3a1c";

    [STAThread]
    private static void Main(string[] args)
    {
#if SELFTEST
        if (NativeMethods.AllocConsole()) { /* 附加到调用方的控制台 */ }
        SelfTest.Run();
#else
        // 开机自启走 --silent：主窗口不弹出、不抢焦点，只在托盘静默就绪。
        bool startHidden = args.Any(a =>
            string.Equals(a, StartupManager.SilentArgument, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(true, MutexName, out bool created);
        if (!created)
        {
            // 已有实例在跑：静默启动（自启）不打扰用户，直接退出；
            // 手动双击时才唤醒它的主窗口。
            if (!startHidden)
                NativeMethods.PostMessage((IntPtr)0xFFFF,
                    unchecked((uint)TrayAppContext.WM_SHOW_FIRST_INSTANCE), IntPtr.Zero, IntPtr.Zero);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetCompatibleTextRenderingDefault(false);

        var cfg = Config.Load();
        // 配置与注册表不一致时同步一次（exe 搬家、Run 项被手动删过等）。
        bool startup = StartupManager.Sync(cfg.RunAtStartup);
        cfg.SaveState(startup);
        var ctx = new TrayAppContext(cfg, startHidden);
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
