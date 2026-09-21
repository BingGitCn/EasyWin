using System.Text.RegularExpressions;
using Microsoft.Win32;
using EasyWin.Models;

namespace EasyWin.Services;

/// <summary>系统调整项(Tweak)的检测与应用:注册表、服务、电源计划、资源管理器。</summary>
public static class TweakService
{
    // ---------------- Windows 更新 ----------------

    private const string UpdatePolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";

    /// <summary>三重保险:组策略 NoAutoUpdate=1 + wuauserv 禁用 + WaaSMedicSvc 禁用(防自动恢复)。</summary>
    public static bool IsUpdateDisabled()
    {
        using var policy = Registry.LocalMachine.OpenSubKey(UpdatePolicyKey);
        var noAuto = policy?.GetValue("NoAutoUpdate") as int?;
        using var medic = Registry.LocalMachine.OpenSubKey($"{ServicesKey}\\WaaSMedicSvc");
        var medicStart = medic?.GetValue("Start") as int?;
        return noAuto == 1 && medicStart == 4;
    }

    /// <summary>返回警告信息(如 WaaSMedicSvc 受保护写入失败),全部成功返回 null。</summary>
    public static string? SetUpdateDisabled(bool disable)
    {
        string? warning = null;

        using (var au = Registry.LocalMachine.CreateSubKey(UpdatePolicyKey))
        {
            if (disable) au.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
            else au.DeleteValue("NoAutoUpdate", throwOnMissingValue: false);
        }

        TrySetServiceStart("wuauserv", disable ? 4 : 3, ref warning);
        TrySetServiceStart("WaaSMedicSvc", disable ? 4 : 3, ref warning);

        if (disable)
        {
            // 停止正在运行的服务(禁用后也要停掉才立即生效)
            CommandRunner.Run("cmd", "/c", "net stop wuauserv");
        }
        return warning;
    }

    private static void TrySetServiceStart(string serviceName, int startValue, ref string? warning)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($"{ServicesKey}\\{serviceName}", writable: true);
            if (key == null)
            {
                warning ??= $"{serviceName} 服务不存在";
                return;
            }
            key.SetValue("Start", startValue, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            Log.Warn($"设置服务 {serviceName}.Start={startValue} 失败:{ex.Message}");
            warning ??= $"{serviceName} 服务受系统保护,未能修改(策略已生效)";
        }
    }

    // ---------------- 快捷方式小箭头 / 前缀 ----------------

    private const string ShellIconsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons";

    public static bool IsShortcutArrowHidden()
    {
        using var key = Registry.LocalMachine.OpenSubKey(ShellIconsKey);
        return key?.GetValue("29") is string;
    }

    public static void SetShortcutArrowHidden(bool hidden)
    {
        using var key = Registry.LocalMachine.CreateSubKey(ShellIconsKey);
        if (hidden) key.SetValue("29", "%windir%\\System32\\shell32.dll,-50", RegistryValueKind.String);
        else key.DeleteValue("29", throwOnMissingValue: false);
    }

    public static bool IsShortcutPrefixHidden()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer");
        return key?.GetValue("Link") is byte[] bytes && bytes.All(b => b == 0);
    }

    public static void SetShortcutPrefixHidden(bool hidden)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer");
        if (hidden) key.SetValue("Link", new byte[] { 0, 0, 0, 0 }, RegistryValueKind.Binary);
        else key.DeleteValue("Link", throwOnMissingValue: false);
    }

    // ---------------- 远程桌面 ----------------

    public static bool IsRemoteDesktopEnabled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server");
        return key?.GetValue("fDenyTSConnections") is 0;
    }

    public static void SetRemoteDesktop(bool enable)
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\Terminal Server", writable: true)
            ?? Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server");
        key.SetValue("fDenyTSConnections", enable ? 0 : 1, RegistryValueKind.DWord);
    }

    // ---------------- 防火墙 ----------------

    /// <summary>三个配置文件的 EnableFirewall 全为 1 视为开启(检测走注册表,不受 netsh 本地化输出影响)。</summary>
    public static bool IsFirewallEnabled()
    {
        foreach (var profile in new[] { "DomainProfile", "StandardProfile", "PublicProfile" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{profile}");
            if (key?.GetValue("EnableFirewall") is not 1) return false;
        }
        return true;
    }

    public static async Task SetFirewallAsync(bool enable) =>
        await CommandRunner.RunAsync("netsh", "advfirewall", "set", "allprofiles",
            "state", enable ? "on" : "off").ConfigureAwait(false);

    // ---------------- 视觉效果 ----------------

    public static bool IsBestPerformance()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
        return key?.GetValue("VisualFXSetting") is 2;
    }

    public static void SetBestPerformance(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
        key.SetValue("VisualFXSetting", enable ? 2 : 0, RegistryValueKind.DWord);
    }

    // ---------------- 电源计划 ----------------

    public static async Task<List<PowerPlan>> GetPowerPlansAsync()
    {
        var list = new List<PowerPlan>();
        var result = await CommandRunner.RunAsync("powercfg", "/list").ConfigureAwait(false);
        foreach (Match m in Regex.Matches(result.Output, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*(?:\(([^)]*)\))?\s*(\*)?"))
            list.Add(new PowerPlan(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value == "*"));
        return list;
    }

    public static async Task SetPowerPlanAsync(string planGuid) =>
        await CommandRunner.RunAsync("powercfg", "/setactive", planGuid).ConfigureAwait(false);

    /// <summary>高性能方案在新系统上可能不存在,缺省时先复制再激活。</summary>
    public static async Task ActivateHighPerformanceAsync()
    {
        const string highPerf = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        var plans = await GetPowerPlansAsync().ConfigureAwait(false);
        if (plans.All(p => !string.Equals(p.Guid, highPerf, StringComparison.OrdinalIgnoreCase)))
        {
            var dup = await CommandRunner.RunAsync("powercfg", "/duplicatescheme", highPerf).ConfigureAwait(false);
            if (!dup.Ok)
                throw new InvalidOperationException("创建高性能电源方案失败:" + dup.AllText);
        }
        await SetPowerPlanAsync(highPerf).ConfigureAwait(false);
    }

    // ---------------- 资源管理器 ----------------

    /// <summary>重启资源管理器(桌面/任务栏会短暂消失再恢复,已打开的文件夹窗口会关闭)。</summary>
    public static void RestartExplorer()
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("explorer"))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* 可能已退出 */ }
            process.Dispose();
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
        });
        Log.Info("已重启资源管理器");
    }
}
