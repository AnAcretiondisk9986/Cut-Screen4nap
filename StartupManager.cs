using Microsoft.Win32;

namespace CutScreen;

/// <summary>
/// 开机自启：写 HKCU\Software\Microsoft\Windows\CurrentVersion\Run。
/// 只影响当前用户，不需要管理员权限，也不需要计划任务。
/// 启动命令统一带 --silent：登录后在托盘静默就绪，不弹主窗口。
/// </summary>
internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CutScreen";

    /// <summary>程序自己用的静默参数；复制到 Run 项里，登录时静默启动。</summary>
    public const string SilentArgument = "--silent";

    /// <summary>自启指向的可执行文件（正式运行时为 CutScreen.exe，开发时为 dotnet 启动的宿主 exe）。</summary>
    public static string ExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "CutScreen.exe");

    /// <summary>写入注册表的命令：路径加引号以兼容含空格的目录。</summary>
    private static string Command =>
        $"\"{ExecutablePath}\" {SilentArgument}";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch { return false; }
    }

    /// <summary>true = 开启自启，false = 移除。返回是否真的写入成功。</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
                           ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return false;

            if (enabled)
                key.SetValue(ValueName, Command, RegistryValueKind.String);
            else if (key.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName, false);

            return IsEnabled() == enabled;
        }
        catch { return false; }
    }

    /// <summary>
    /// 配置与注册表不一致时（移动了 exe 位置、别人手动删了 Run 项）同步一次。
    /// 返回同步后是否处于开启状态。
    /// </summary>
    public static bool Sync(bool desired)
    {
        bool actual = IsEnabled();
        if (actual == desired)
        {
            // 即使状态一致，exe 被搬到别处了也要把命令行更新成当前路径
            if (desired && !CommandEqualsCurrentPath())
                return SetEnabled(true);
            return desired;
        }
        return SetEnabled(desired) && desired;
    }

    private static bool CommandEqualsCurrentPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            var value = key?.GetValue(ValueName) as string;
            return string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
