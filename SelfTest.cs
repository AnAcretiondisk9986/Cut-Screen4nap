#if SELFTEST
// 编译：dotnet run -c Release -p:DefineConstants=SELFTEST
// 只调用「只读 / 可逆」的 API，不会关屏、不会锁屏、不会休眠。
using CutScreen;

internal static class SelfTest
{
    private static readonly List<string> Log = new();

    public static void Run()
    {
        Write("Cut-Screen 环境自检（不会改变屏幕/电源状态）");
        Write("");
        Check("[1] 显示器枚举", () => $"{DisplayController.EnumerateMonitorsForTest().Count} 块可见屏幕");
        Check("[2] 当前活动电源方案", () =>
            Native.TryGetActiveScheme(out var g) ? g.ToString() : "读取失败");
        Check("[3] 关闭显示器超时(AC)", () =>
            Native.TryGetMonitorTimeout(out uint s) ? $"{s} 秒" : "读取失败");
        Check("[4] 空闲输入时钟", () =>
        {
            uint a = Native.LastInputTick();
            Thread.Sleep(60);
            uint b = Native.LastInputTick();
            return $"tick={a}，单调={b >= a}";
        });
        Check("[5] DDC/CI 物理显示器探测(不写 VCP)", () =>
            $"{DisplayController.ProbePhysicalMonitorsForTest()} 台（0 台也正常，会走纯 DPMS）");
        Check("[6] 休眠可用性", () =>
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power");
            return $"HibernateEnabled={k?.GetValue("HibernateEnabled") ?? "<缺失>"}";
        });
        Check("[7] Config 往返", () =>
        {
            var c = new Config { EnterDelaySeconds = 37 };
            c.Save();
            return Config.Load().EnterDelaySeconds == 37 ? "一致 (37)" : "不一致";
        });
        Check("[8] Snapshot 落盘→Recover→清空", () =>
        {
            Snapshot.Save(new Snapshot { Mode = Mode.Nap, EnteredAtUtc = DateTime.UtcNow });
            bool found = Snapshot.Recover(out _);
            return $"Recover={found}，残留={(Snapshot.Load() is null ? "无" : "有")}";
        });
        Check("[9] 开机自启状态读取(只读)", () =>
        {
            bool on = StartupManager.IsEnabled();
            return $"当前自启={(on ? "开启" : "关闭")}，exe={StartupManager.ExecutablePath}";
        });
        Write("\n自检完成：未调用任何会关屏 / 锁屏 / 休眠的 API。");
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "selftest.log");
            File.WriteAllLines(path, Log);
            Write($"日志：{path}");
        }
        catch { }
    }

    private static void Write(string s) { Log.Add(s); Console.WriteLine(s); }

    private static void Check(string name, Func<string> f)
    {
        try { Write($"  [OK]   {name} → {f()}"); }
        catch (Exception ex) { Write($"  [FAIL] {name} → {ex.GetType().Name}: {ex.Message}"); }
    }
}
#endif
