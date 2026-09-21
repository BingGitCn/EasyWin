using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace EasyWin.Services;

public record SysProxyInfo(bool Enabled, string Server, string Override, string? AutoConfigUrl);

/// <summary>系统代理(HTTP)读取与设置,写完注册表后广播 WinINet 使其立即生效。</summary>
public static class SysProxyService
{
    private const string InternetSettingsPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    public static SysProxyInfo Get()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsPath);
        return new SysProxyInfo(
            Enabled: key?.GetValue("ProxyEnable") is 1,
            Server: key?.GetValue("ProxyServer") as string ?? "",
            Override: key?.GetValue("ProxyOverride") as string ?? "",
            AutoConfigUrl: key?.GetValue("AutoConfigURL") as string);
    }

    public static void Set(bool enabled, string server, string bypassList)
    {
        using var key = Registry.CurrentUser.CreateSubKey(InternetSettingsPath);
        key.SetValue("ProxyEnable", enabled ? 1 : 0, RegistryValueKind.DWord);
        if (enabled)
        {
            key.SetValue("ProxyServer", server, RegistryValueKind.String);
            key.SetValue("ProxyOverride", bypassList, RegistryValueKind.String);
        }
        BroadcastChange();
        Log.Info($"系统代理已{(enabled ? "开启: " + server : "关闭")}");
    }

    /// <summary>通知系统代理设置已变化并刷新,否则要重开浏览器才生效。</summary>
    private static void BroadcastChange()
    {
        InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
    }

    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionRefresh = 37;

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int option, IntPtr buffer, int bufferLength);
}
