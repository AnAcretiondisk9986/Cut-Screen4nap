using System.Text.Json;

namespace CutScreen;

internal enum Mode { Hibernate = 0, StayAwake = 1, Nap = 2 }

/// <summary>用户设置。存于 %AppData%\CutScreen\config.json。</summary>
internal sealed class Config
{
    public int EnterDelaySeconds { get; set; } = 20;
    public int ExitGraceSeconds { get; set; } = 3;
    public bool RememberLastMode { get; set; } = true;
    public Mode LastMode { get; set; } = Mode.Nap;

    /// <summary>B 档默认不锁屏：用户要求「鼠标移动或键盘输入任意值即可」退出。</summary>
    public bool StayAwakeLockScreen { get; set; } = false;

    /// <summary>默认关：先在设置页跑一次「DDC/CI 校准」确认能写回再开，避免第一晚背光叫不醒。</summary>
    public bool UseDdcCi { get; set; } = false;
    public bool IgnoreMouseJitter { get; set; } = false;
    public int MouseThresholdPx { get; set; } = 4;

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CutScreen");

    private static string Path_ => Path.Combine(Dir, "config.json");

    public static Config Load()
    {
        try
        {
            if (File.Exists(Path_))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(Path_)) ?? new Config();
        }
        catch { /* 损坏则用默认值 */ }
        return new Config();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path_, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 设置写不进去不该阻挡主功能 */ }
    }
}

/// <summary>
/// 持久化系统改动的快照。只有「改了全局设置」的档位需要。
/// 进程崩溃/断电后，下次启动先还原再工作。
/// </summary>
internal sealed class Snapshot
{
    public Mode Mode { get; set; }
    public DateTime EnteredAtUtc { get; set; }
    public bool MonitorTimeoutChanged { get; set; }
    public uint MonitorTimeoutOriginalSeconds { get; set; }
    public bool DdcCiWrittenOff { get; set; }

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CutScreen");

    private static string Path_ => Path.Combine(Dir, "snapshot.json");

    public static Snapshot? Load()
    {
        try
        {
            if (File.Exists(Path_))
                return JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(Path_));
        }
        catch { }
        return null;
    }

    public static void Save(Snapshot s) => SaveCore(s);

    private static void SaveCore(Snapshot? s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (s is null)
            {
                if (File.Exists(Path_)) File.Delete(Path_);
            }
            else
            {
                var tmp = Path_ + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(s));
                File.Move(tmp, Path_, true);
            }
        }
        catch { }
    }

    public static void Clear() => SaveCore(null);

    /// <summary>启动时调用：还原任何残留的持久化改动。</summary>
    public static bool Recover(out string report)
    {
        var s = Load();
        report = "";
        if (s is null) return false;

        var notes = new List<string>();
        if (s.MonitorTimeoutChanged)
        {
            if (Native.TrySetMonitorTimeout(s.MonitorTimeoutOriginalSeconds))
                notes.Add($"显示器超时已还原为 {s.MonitorTimeoutOriginalSeconds}s");
            else
                notes.Add("警告：显示器超时还原失败，请手动检查电源选项");
        }
        if (s.DdcCiWrittenOff)
        {
            // 进程级句柄已随崩溃消失，这里只做一次尽力写回
            int n = DisplayController.TryRestoreAllVcp();
            notes.Add(n > 0 ? $"已向 {n} 台显示器写回 VCP 0x01" : "未发现可写回的 DDC/CI 显示器");
        }
        Clear();
        report = $"检测到上次【{ModeNames.Cn(s.Mode)}】异常退出（{s.EnteredAtUtc.ToLocalTime():MM-dd HH:mm}），已自动还原：" + string.Join("；", notes);
        return true;
    }
}

internal static class ModeNames
{
    public static string Cn(Mode m) => m switch
    {
        Mode.Hibernate => "休眠",
        Mode.StayAwake => "挂机",
        Mode.Nap => "午睡",
        _ => "未知"
    };
}
