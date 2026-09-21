using System.IO;

namespace EasyWin.Services;

/// <summary>
/// 开机自启:通过任务计划程序(schtasks)在用户登录时以最高权限启动,
/// 相比注册表 Run 键不会在登录时弹 UAC;自动化规则、电源自动切换等后台功能依赖常驻。
/// </summary>
public static class AutoStartService
{
    private const string TaskName = "EasyWin_AutoStart";

    private static string ExePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "EasyWin.exe");

    public static bool IsEnabled()
    {
        var result = CommandRunner.Run("schtasks", "/Query", "/TN", TaskName);
        return result.Ok;
    }

    public static (bool ok, string message) Enable()
    {
        if (IsEnabled()) return (true, "开机自启已是开启状态");
        // /TR 内层引号包住含空格的 exe 路径,并带 --tray 参数直接进入托盘
        var result = CommandRunner.Run("schtasks", "/Create", "/TN", TaskName,
            "/TR", $"\\\"{ExePath}\\\" --tray",
            "/SC", "ONLOGON", "/RL", "HIGHEST", "/F");
        return result.Ok
            ? (true, "已开启开机自启(登录后直接进入托盘)")
            : (false, "创建计划任务失败:" + result.AllText);
    }

    public static (bool ok, string message) Disable()
    {
        if (!IsEnabled()) return (true, "开机自启已是关闭状态");
        var result = CommandRunner.Run("schtasks", "/Delete", "/TN", TaskName, "/F");
        return result.Ok
            ? (true, "已关闭开机自启")
            : (false, "删除计划任务失败:" + result.AllText);
    }
}
