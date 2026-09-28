using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using EasyWin.Models;

namespace EasyWin.Services;

/// <summary>系统调整项(Tweak)的检测与应用:注册表、服务、电源计划、资源管理器。</summary>
public static class TweakService
{
    // ---------------- 通用工具 ----------------

    private const string AdvancedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    /// <summary>Win11 = NT 内核 Build 22000+。任务栏对齐/小组件/经典右键菜单等卡片仅对 Win11 有意义。</summary>
    public static bool IsWin11 => Environment.OSVersion.Version.Build >= 22000;

    private static int? Dword(RegistryKey root, string path, string name)
    {
        using var key = root.OpenSubKey(path);
        return key?.GetValue(name) as int?;
    }

    private static void WriteDword(RegistryKey root, string path, string name, int value)
    {
        using var key = root.CreateSubKey(path);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static void DeleteValue(RegistryKey root, string path, string name)
    {
        using var key = root.OpenSubKey(path, writable: true) ?? root.CreateSubKey(path);
        key.DeleteValue(name, throwOnMissingValue: false);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string? lParam,
        uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    /// <summary>广播设置变更(WM_SETTINGCHANGE),让任务栏/资源管理器的部分注册表修改免重启生效。</summary>
    private static void BroadcastSettingChange(string section = "Tray Settings")
    {
        SendMessageTimeout(new IntPtr(0xFFFF) /*HWND_BROADCAST*/, 0x001A /*WM_SETTINGCHANGE*/, UIntPtr.Zero,
            section, 0x0002 /*SMTO_ABORTIFHUNG*/, 2000, out _);
    }

    /// <summary>Win11 23H2(Build 22631+):任务栏右键「结束任务」等。</summary>
    public static bool IsWin11_23H2 => Environment.OSVersion.Version.Build >= 22631;

    /// <summary>Win11 24H2(Build 26100+):Sudo 等。</summary>
    public static bool IsWin11_24H2 => Environment.OSVersion.Version.Build >= 26100;

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

    private static void TrySetServiceStart(string serviceName, int startValue, ref string? warning, bool warnIfMissing = true)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($"{ServicesKey}\\{serviceName}", writable: true);
            if (key == null)
            {
                if (warnIfMissing) warning ??= $"{serviceName} 服务不存在";
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
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\Terminal Server");
        return key?.GetValue("fDenyTSConnections") is 0;
    }

    /// <summary>开关远程桌面;开启时同步放行防火墙 RDP 入站规则(规则用程序化名称,不受系统语言影响)——
    /// 否则开着防火墙的机器即使 fDenyTSConnections=0 也连不上。参考 microsoft/WindowsDeveloperConfig 的注记。</summary>
    public static async Task SetRemoteDesktopAsync(bool enable)
    {
        using (var key = Registry.LocalMachine.OpenSubKey(
                   @"SYSTEM\CurrentControlSet\Control\Terminal Server", writable: true)
               ?? Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server"))
        {
            key.SetValue("fDenyTSConnections", enable ? 0 : 1, RegistryValueKind.DWord);
        }
        foreach (var rule in new[] { "RemoteDesktop-UserMode-In-TCP", "RemoteDesktop-UserMode-In-UDP" })
            await CommandRunner.RunAsync("netsh", "advfirewall", "firewall", "set", "rule",
                $"name={rule}", "new", enable ? "enable=Yes" : "enable=No").ConfigureAwait(false);
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

    /// <summary>当前活动计划的 GUID(读取失败返回 null)。</summary>
    public static async Task<string?> GetActivePlanGuidAsync()
    {
        var result = await CommandRunner.RunAsync("powercfg", "/getactivescheme").ConfigureAwait(false);
        if (!result.Ok) return null;
        var m = Regex.Match(result.Output, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        return m.Success ? m.Groups[1].Value : null;
    }

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

    /// <summary>显示文件扩展名(HideFileExt=0)。写入后广播设置,通常立即生效。</summary>
    public static bool IsFileExtensionShown() => Dword(Registry.CurrentUser, AdvancedPath, "HideFileExt") == 0;

    public static void SetFileExtensionShown(bool show)
    {
        WriteDword(Registry.CurrentUser, AdvancedPath, "HideFileExt", show ? 0 : 1);
        BroadcastSettingChange();
    }

    /// <summary>显示隐藏文件(Hidden=1,默认 2)。</summary>
    public static bool IsHiddenFilesShown() => Dword(Registry.CurrentUser, AdvancedPath, "Hidden") == 1;

    public static void SetHiddenFilesShown(bool show)
    {
        WriteDword(Registry.CurrentUser, AdvancedPath, "Hidden", show ? 1 : 2);
        BroadcastSettingChange();
    }

    private const string NameSpace3DKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\{0DB7E03F-FC29-4DC6-9020-FF41B59E513A}";

    /// <summary>隐藏「3D 对象」文件夹:删除 This PC 下的 NameSpace 注册表项(需重启资源管理器)。</summary>
    public static bool Is3DObjectsHidden()
    {
        using var key = Registry.LocalMachine.OpenSubKey(NameSpace3DKey);
        return key == null;
    }

    public static void Set3DObjectsHidden(bool hidden)
    {
        if (hidden)
            Registry.LocalMachine.DeleteSubKeyTree(NameSpace3DKey, throwOnMissingSubKey: false);
        else
            Registry.LocalMachine.CreateSubKey(NameSpace3DKey);
    }

    private const string ClassicContextMenuKey =
        @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

    /// <summary>Win11 恢复 Win10 经典右键菜单:注册旧版上下文菜单 COM 重定向,默认值为空字符串。</summary>
    public static bool IsClassicContextMenuEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ClassicContextMenuKey);
        return key != null;
    }

    public static void SetClassicContextMenu(bool enable)
    {
        if (enable)
        {
            using var key = Registry.CurrentUser.CreateSubKey(ClassicContextMenuKey);
            key.SetValue(string.Empty, string.Empty, RegistryValueKind.String);
        }
        else
        {
            Registry.CurrentUser.DeleteSubKeyTree(
                @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", throwOnMissingSubKey: false);
        }
    }

    private const string TakeOwnershipFileKey = @"*\shell\TakeOwnership";
    private const string TakeOwnershipDirKey = @"Directory\shell\TakeOwnership";

    private const string TakeOwnershipFileCommand =
        @"cmd.exe /c takeown /f ""%1"" && icacls ""%1"" /grant *S-1-5-32-544:F";
    private const string TakeOwnershipDirCommand =
        @"cmd.exe /c takeown /f ""%1"" /r /d y && icacls ""%1"" /grant *S-1-5-32-544:F /t";

    /// <summary>右键菜单「管理员取得所有权」(文件+目录)。用 Administrators 组 SID,不受系统语言影响。</summary>
    public static bool IsTakeOwnershipMenuEnabled()
    {
        using var key = Registry.ClassesRoot.OpenSubKey(TakeOwnershipFileKey);
        return key != null;
    }

    public static void SetTakeOwnershipMenu(bool enable)
    {
        if (enable)
        {
            using (var key = Registry.ClassesRoot.CreateSubKey(TakeOwnershipFileKey))
            {
                key.SetValue(string.Empty, "管理员取得所有权");
                key.SetValue("NoWorkingDirectory", string.Empty);
                using var cmd = key.CreateSubKey("command");
                cmd.SetValue(string.Empty, TakeOwnershipFileCommand);
            }
            using (var key = Registry.ClassesRoot.CreateSubKey(TakeOwnershipDirKey))
            {
                key.SetValue(string.Empty, "管理员取得所有权");
                key.SetValue("NoWorkingDirectory", string.Empty);
                using var cmd = key.CreateSubKey("command");
                cmd.SetValue(string.Empty, TakeOwnershipDirCommand);
            }
        }
        else
        {
            Registry.ClassesRoot.DeleteSubKeyTree(TakeOwnershipFileKey, throwOnMissingSubKey: false);
            Registry.ClassesRoot.DeleteSubKeyTree(TakeOwnershipDirKey, throwOnMissingSubKey: false);
        }
    }

    private const string CmdHereKey = @"Directory\Background\shell\OpenCmdHere";

    /// <summary>右键菜单「在此处打开 CMD」(文件夹背景,pushd 到当前目录)。</summary>
    public static bool IsCmdHereMenuEnabled()
    {
        using var key = Registry.ClassesRoot.OpenSubKey(CmdHereKey);
        return key != null;
    }

    public static void SetCmdHereMenu(bool enable)
    {
        if (enable)
        {
            using var key = Registry.ClassesRoot.CreateSubKey(CmdHereKey);
            key.SetValue(string.Empty, "在此处打开 CMD");
            key.SetValue("Icon", "cmd.exe");
            using var cmd = key.CreateSubKey("command");
            cmd.SetValue(string.Empty, @"cmd.exe /s /k pushd ""%V""");
        }
        else
        {
            Registry.ClassesRoot.DeleteSubKeyTree(CmdHereKey, throwOnMissingSubKey: false);
        }
    }

    // ---------------- 任务栏与开始菜单 ----------------

    /// <summary>任务栏图标靠左(TaskbarAl=0,默认居中)。仅 Win11 有效。</summary>
    public static bool IsTaskbarLeft() => Dword(Registry.CurrentUser, AdvancedPath, "TaskbarAl") == 0;

    public static void SetTaskbarLeft(bool left)
    {
        WriteDword(Registry.CurrentUser, AdvancedPath, "TaskbarAl", left ? 0 : 1);
        BroadcastSettingChange();
    }

    /// <summary>任务栏时钟显示秒(ShowSecondsInSystemClock=1)。</summary>
    public static bool IsClockSecondsShown() => Dword(Registry.CurrentUser, AdvancedPath, "ShowSecondsInSystemClock") == 1;

    public static void SetClockSecondsShown(bool show)
    {
        WriteDword(Registry.CurrentUser, AdvancedPath, "ShowSecondsInSystemClock", show ? 1 : 0);
        BroadcastSettingChange();
    }

    private const string SearchKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Search";

    /// <summary>隐藏任务栏搜索框/按钮(SearchboxTaskbarMode=0;1=图标,2=搜索框)。</summary>
    public static bool IsTaskbarSearchHidden() => Dword(Registry.CurrentUser, SearchKeyPath, "SearchboxTaskbarMode") == 0;

    public static void SetTaskbarSearchHidden(bool hidden)
    {
        WriteDword(Registry.CurrentUser, SearchKeyPath, "SearchboxTaskbarMode", hidden ? 0 : IsWin11 ? 1 : 2);
        BroadcastSettingChange();
    }

    /// <summary>隐藏任务栏「任务视图」按钮(ShowTaskViewButton=0)。</summary>
    public static bool IsTaskViewHidden() => Dword(Registry.CurrentUser, AdvancedPath, "ShowTaskViewButton") == 0;

    public static void SetTaskViewHidden(bool hidden)
    {
        WriteDword(Registry.CurrentUser, AdvancedPath, "ShowTaskViewButton", hidden ? 0 : 1);
        BroadcastSettingChange();
    }

    /// <summary>隐藏任务栏「小组件」按钮(TaskbarDa=0)。仅 Win11。</summary>
    public static bool IsWidgetsHidden() => Dword(Registry.CurrentUser, AdvancedPath, "TaskbarDa") == 0;

    public static void SetWidgetsHidden(bool hidden)
    {
        WriteDword(Registry.CurrentUser, AdvancedPath, "TaskbarDa", hidden ? 0 : 1);
        BroadcastSettingChange();
    }

    /// <summary>隐藏任务栏「聊天 / Copilot」按钮(TaskbarMn=0 + ShowCopilotButton=0,不同版本按钮其一)。</summary>
    public static bool IsChatCopilotHidden() =>
        Dword(Registry.CurrentUser, AdvancedPath, "TaskbarMn") == 0
        && Dword(Registry.CurrentUser, AdvancedPath, "ShowCopilotButton") is null or 0;

    public static void SetChatCopilotHidden(bool hidden)
    {
        WriteDword(Registry.CurrentUser, AdvancedPath, "TaskbarMn", hidden ? 0 : 1);
        if (hidden) WriteDword(Registry.CurrentUser, AdvancedPath, "ShowCopilotButton", 0);
        else DeleteValue(Registry.CurrentUser, AdvancedPath, "ShowCopilotButton");
        BroadcastSettingChange();
    }

    private const string ContentDeliveryPath = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    /// <summary>关闭开始菜单「推荐项目/建议」与账户通知:Win11 走 Start_IrisRecommendations/Start_AccountNotifications,Win10 走内容投递管理器。</summary>
    public static bool IsStartRecommendationsHidden() => IsWin11
        ? Dword(Registry.CurrentUser, AdvancedPath, "Start_IrisRecommendations") == 0
        : Dword(Registry.CurrentUser, ContentDeliveryPath, "SystemPaneSuggestionsEnabled") == 0;

    public static void SetStartRecommendationsHidden(bool hidden)
    {
        if (IsWin11)
        {
            WriteDword(Registry.CurrentUser, AdvancedPath, "Start_IrisRecommendations", hidden ? 0 : 1);
            WriteDword(Registry.CurrentUser, AdvancedPath, "Start_AccountNotifications", hidden ? 0 : 1);
        }
        else
        {
            if (hidden)
            {
                WriteDword(Registry.CurrentUser, ContentDeliveryPath, "SystemPaneSuggestionsEnabled", 0);
                WriteDword(Registry.CurrentUser, ContentDeliveryPath, "SubscribedContent-338388Enabled", 0);
                WriteDword(Registry.CurrentUser, ContentDeliveryPath, "SubscribedContent-338389Enabled", 0);
            }
            else
            {
                DeleteValue(Registry.CurrentUser, ContentDeliveryPath, "SystemPaneSuggestionsEnabled");
                DeleteValue(Registry.CurrentUser, ContentDeliveryPath, "SubscribedContent-338388Enabled");
                DeleteValue(Registry.CurrentUser, ContentDeliveryPath, "SubscribedContent-338389Enabled");
            }
        }
        BroadcastSettingChange();
    }

    // ---------------- 隐私 ----------------

    private const string DataCollectionPath = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection";

    /// <summary>关闭遥测:组策略 AllowTelemetry=0(Pro/Home 实际落到最低档「必需」)+ 禁用 DiagTrack/dmwappushservice。</summary>
    public static bool IsTelemetryDisabled() => Dword(Registry.LocalMachine, DataCollectionPath, "AllowTelemetry") == 0;

    public static string? SetTelemetryDisabled(bool disable)
    {
        string? warning = null;
        using (var key = Registry.LocalMachine.CreateSubKey(DataCollectionPath))
        {
            if (disable) key.SetValue("AllowTelemetry", 0, RegistryValueKind.DWord);
            else key.DeleteValue("AllowTelemetry", throwOnMissingValue: false);
        }
        TrySetServiceStart("DiagTrack", disable ? 4 : 2, ref warning);
        TrySetServiceStart("dmwappushservice", disable ? 4 : 3, ref warning, warnIfMissing: false);
        if (disable) CommandRunner.Run("cmd", "/c", "net stop DiagTrack");
        return warning;
    }

    /// <summary>禁用广告 ID(HKCU AdvertisingInfo Enabled=0),应用不再拿到跨应用广告标识。</summary>
    public static bool IsAdvertisingIdDisabled() =>
        Dword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled") == 0;

    public static void SetAdvertisingIdDisabled(bool disable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo");
        key.SetValue("Enabled", disable ? 0 : 1, RegistryValueKind.DWord);
    }

    private const string LocationSensorsPath = @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors";

    /// <summary>禁用位置跟踪(组策略 DisableLocation=1,同时切断系统定位服务)。</summary>
    public static bool IsLocationTrackingDisabled() => Dword(Registry.LocalMachine, LocationSensorsPath, "DisableLocation") == 1;

    public static void SetLocationTrackingDisabled(bool disable)
    {
        if (disable) WriteDword(Registry.LocalMachine, LocationSensorsPath, "DisableLocation", 1);
        else DeleteValue(Registry.LocalMachine, LocationSensorsPath, "DisableLocation");
    }

    private const string ActivityFeedPath = @"SOFTWARE\Policies\Microsoft\Windows\System";

    /// <summary>禁用活动历史记录/时间线:不采集、不上传(EnableActivityFeed=0 等)。</summary>
    public static bool IsActivityHistoryDisabled() =>
        Dword(Registry.LocalMachine, ActivityFeedPath, "EnableActivityFeed") == 0;

    public static void SetActivityHistoryDisabled(bool disable)
    {
        if (disable)
        {
            WriteDword(Registry.LocalMachine, ActivityFeedPath, "EnableActivityFeed", 0);
            WriteDword(Registry.LocalMachine, ActivityFeedPath, "PublishUserActivities", 0);
            WriteDword(Registry.LocalMachine, ActivityFeedPath, "UploadUserActivities", 0);
        }
        else
        {
            DeleteValue(Registry.LocalMachine, ActivityFeedPath, "EnableActivityFeed");
            DeleteValue(Registry.LocalMachine, ActivityFeedPath, "PublishUserActivities");
            DeleteValue(Registry.LocalMachine, ActivityFeedPath, "UploadUserActivities");
        }
    }

    private const string WindowsSearchPolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\Windows Search";

    /// <summary>禁用 Cortana(组策略 AllowCortana=0)。</summary>
    public static bool IsCortanaDisabled() => Dword(Registry.LocalMachine, WindowsSearchPolicyPath, "AllowCortana") == 0;

    public static void SetCortanaDisabled(bool disable)
    {
        if (disable) WriteDword(Registry.LocalMachine, WindowsSearchPolicyPath, "AllowCortana", 0);
        else DeleteValue(Registry.LocalMachine, WindowsSearchPolicyPath, "AllowCortana");
    }

    /// <summary>禁用 Windows 错误报告(WER Disabled=1,不再向微软发送错误报告)。</summary>
    public static bool IsErrorReportingDisabled() =>
        Dword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled") == 1;

    public static void SetErrorReportingDisabled(bool disable)
    {
        if (disable) WriteDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1);
        else DeleteValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled");
    }

    /// <summary>开始菜单搜索只显示本地结果(DisableSearchBoxSuggestions=1,禁用 Bing 网页建议)。</summary>
    public static bool IsWebSearchSuggestionsDisabled() =>
        Dword(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions") == 1;

    public static void SetWebSearchSuggestionsDisabled(bool disable)
    {
        if (disable) WriteDword(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1);
        else DeleteValue(Registry.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions");
        BroadcastSettingChange();
    }

    // ---------------- 系统行为 ----------------

    private const string PowerKeyPath = @"SYSTEM\CurrentControlSet\Control\Power";

    /// <summary>禁用休眠(powercfg /h off),同时关闭快速启动。HibernateEnabled:1=开,0=关。</summary>
    public static bool IsHibernateDisabled() => Dword(Registry.LocalMachine, PowerKeyPath, "HibernateEnabled") == 0;

    public static async Task SetHibernateDisabledAsync(bool disable) =>
        await CommandRunner.RunAsync("powercfg", "/h", disable ? "off" : "on").ConfigureAwait(false);

    /// <summary>关闭自动播放(AutoplayHandlers DisableAutoplay=1,即系统设置里的自动播放总开关)。</summary>
    public static bool IsAutoplayDisabled() =>
        Dword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers", "DisableAutoplay") == 1;

    public static void SetAutoplayDisabled(bool disable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers");
        key.SetValue("DisableAutoplay", disable ? 1 : 0, RegistryValueKind.DWord);
    }

    private const string SmartScreenPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";

    /// <summary>禁用 SmartScreen(SmartScreenEnabled=Off)。关闭后下载/运行陌生程序不再拦截,请自行承担风险。</summary>
    public static bool IsSmartScreenDisabled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(SmartScreenPath);
        return key?.GetValue("SmartScreenEnabled") is string s && s.Equals("Off", StringComparison.OrdinalIgnoreCase);
    }

    public static void SetSmartScreenDisabled(bool disable)
    {
        using var key = Registry.LocalMachine.CreateSubKey(SmartScreenPath);
        key.SetValue("SmartScreenEnabled", disable ? "Off" : "RequireAdmin", RegistryValueKind.String);
    }

    private const string HvciPath =
        @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";

    /// <summary>禁用内存完整性(HVCI Enabled=0,需重启生效)。恢复时删除键值回到系统默认。</summary>
    public static bool IsMemoryIntegrityDisabled() => Dword(Registry.LocalMachine, HvciPath, "Enabled") == 0;

    public static void SetMemoryIntegrityDisabled(bool disable)
    {
        if (disable) WriteDword(Registry.LocalMachine, HvciPath, "Enabled", 0);
        else DeleteValue(Registry.LocalMachine, HvciPath, "Enabled");
    }

    private const string SystemRestorePath = @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore";

    /// <summary>禁用系统还原(组策略 DisableSR=1)。关闭后将无法用还原点回滚系统,慎用。</summary>
    public static bool IsSystemRestoreDisabled() => Dword(Registry.LocalMachine, SystemRestorePath, "DisableSR") == 1;

    public static void SetSystemRestoreDisabled(bool disable)
    {
        if (disable) WriteDword(Registry.LocalMachine, SystemRestorePath, "DisableSR", 1);
        else DeleteValue(Registry.LocalMachine, SystemRestorePath, "DisableSR");
    }

    // ---------------- 开发与系统增强(键值参考 microsoft/WindowsDeveloperConfig) ----------------

    private const string AppModelUnlockPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock";

    /// <summary>开发者模式(AllowDevelopmentWithoutDevLicense=1):免签名侧加载应用、符号链接等开发特性。</summary>
    public static bool IsDeveloperModeEnabled() => Dword(Registry.LocalMachine, AppModelUnlockPath, "AllowDevelopmentWithoutDevLicense") == 1;

    public static void SetDeveloperMode(bool enable)
    {
        using var key = Registry.LocalMachine.CreateSubKey(AppModelUnlockPath);
        key.SetValue("AllowDevelopmentWithoutDevLicense", enable ? 1 : 0, RegistryValueKind.DWord);
    }

    /// <summary>启用 Win32 长路径(LongPathsEnabled=1),解除 260 字符路径限制,需重启生效。</summary>
    public static bool IsLongPathsEnabled() => Dword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled") == 1;

    public static void SetLongPaths(bool enable)
    {
        if (enable) WriteDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 1);
        else DeleteValue(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled");
    }

    private const string SudoPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Sudo";

    /// <summary>启用 Win11 Sudo(Enabled=3 内联模式,同官方开发机配置;0=关闭)。仅 24H2+。</summary>
    public static bool IsSudoEnabled() => (Dword(Registry.LocalMachine, SudoPath, "Enabled") ?? 0) >= 1;

    public static void SetSudo(bool enable)
    {
        using var key = Registry.LocalMachine.CreateSubKey(SudoPath);
        key.SetValue("Enabled", enable ? 3 : 0, RegistryValueKind.DWord);
    }

    private const string PersonalizePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>系统深色模式(应用与系统同时 AppsUseLightTheme=0)。写入后广播,多数应用即时换色。</summary>
    public static bool IsSystemDarkMode() =>
        Dword(Registry.CurrentUser, PersonalizePath, "AppsUseLightTheme") == 0
        && Dword(Registry.CurrentUser, PersonalizePath, "SystemUsesLightTheme") == 0;

    public static void SetSystemDarkMode(bool dark)
    {
        using var key = Registry.CurrentUser.CreateSubKey(PersonalizePath);
        key.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        key.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        BroadcastSettingChange("Windows");
    }

    /// <summary>勿扰模式:关闭全部横幅通知(NOC_GLOBAL_SETTING_TOASTS_ENABLED=0,即系统「通知」总开关)。</summary>
    public static bool IsNotificationsOff() =>
        Dword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_TOASTS_ENABLED") == 0;

    public static void SetNotificationsOff(bool off)
    {
        if (off) WriteDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_TOASTS_ENABLED", 0);
        else DeleteValue(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_TOASTS_ENABLED");
    }

    // ---------------- 资源管理器增强(键值参考 microsoft/WindowsDeveloperConfig) ----------------

    private const string CabinetStatePath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState";

    /// <summary>资源管理器默认打开「此电脑」(LaunchTo=1,默认快速访问)。</summary>
    public static bool IsLaunchToThisPc() => Dword(Registry.CurrentUser, AdvancedPath, "LaunchTo") == 1;

    public static void SetLaunchToThisPc(bool thisPc)
    {
        if (thisPc) WriteDword(Registry.CurrentUser, AdvancedPath, "LaunchTo", 1);
        else DeleteValue(Registry.CurrentUser, AdvancedPath, "LaunchTo");
        BroadcastSettingChange();
    }

    /// <summary>标题栏显示完整路径(CabinetState FullPath=1)。</summary>
    public static bool IsFullPathInTitleBar() => Dword(Registry.CurrentUser, CabinetStatePath, "FullPath") == 1;

    public static void SetFullPathInTitleBar(bool show)
    {
        if (show) WriteDword(Registry.CurrentUser, CabinetStatePath, "FullPath", 1);
        else DeleteValue(Registry.CurrentUser, CabinetStatePath, "FullPath");
        BroadcastSettingChange();
    }

    /// <summary>精简快速访问与推广提示:常用文件夹/最近文件/云文件推荐/OneDrive 同步提示全关。</summary>
    public static bool IsQuickAccessLean() =>
        Dword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowFrequent") == 0
        && Dword(Registry.CurrentUser, AdvancedPath, "ShowSyncProviderNotifications") == 0;

    public static void SetQuickAccessLean(bool lean)
    {
        var explorerPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
        if (lean)
        {
            WriteDword(Registry.CurrentUser, explorerPath, "ShowFrequent", 0);
            WriteDword(Registry.CurrentUser, explorerPath, "ShowRecent", 0);
            WriteDword(Registry.CurrentUser, explorerPath, "ShowCloudFilesInQuickAccess", 0);
            WriteDword(Registry.CurrentUser, AdvancedPath, "ShowSyncProviderNotifications", 0);
        }
        else
        {
            DeleteValue(Registry.CurrentUser, explorerPath, "ShowFrequent");
            DeleteValue(Registry.CurrentUser, explorerPath, "ShowRecent");
            DeleteValue(Registry.CurrentUser, explorerPath, "ShowCloudFilesInQuickAccess");
            DeleteValue(Registry.CurrentUser, AdvancedPath, "ShowSyncProviderNotifications");
        }
        BroadcastSettingChange();
    }

    private const string SearchSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\SearchSettings";

    /// <summary>关闭搜索亮点(IsDynamicSearchBoxEnabled=0,搜索框不再轮播热点图)。</summary>
    public static bool IsSearchHighlightsOff() => Dword(Registry.CurrentUser, SearchSettingsPath, "IsDynamicSearchBoxEnabled") == 0;

    public static void SetSearchHighlightsOff(bool off)
    {
        if (off) WriteDword(Registry.CurrentUser, SearchSettingsPath, "IsDynamicSearchBoxEnabled", 0);
        else DeleteValue(Registry.CurrentUser, SearchSettingsPath, "IsDynamicSearchBoxEnabled");
        BroadcastSettingChange();
    }

    private const string TaskbarDevSettingsPath = AdvancedPath + @"\TaskbarDeveloperSettings";

    /// <summary>任务栏右键「结束任务」(TaskbarEndTask=1,Win11 23H2+)。</summary>
    public static bool IsTaskbarEndTaskEnabled() => Dword(Registry.CurrentUser, TaskbarDevSettingsPath, "TaskbarEndTask") == 1;

    public static void SetTaskbarEndTask(bool enable)
    {
        if (enable) WriteDword(Registry.CurrentUser, TaskbarDevSettingsPath, "TaskbarEndTask", 1);
        else DeleteValue(Registry.CurrentUser, TaskbarDevSettingsPath, "TaskbarEndTask");
        BroadcastSettingChange();
    }

    // ---------------- 资源管理器进程 ----------------

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
