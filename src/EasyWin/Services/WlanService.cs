using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using EasyWin.Models;

namespace EasyWin.Services;

public record WlanNetwork(string Ssid, string Auth, int Signal, bool Connected)
{
    public string SignalText => $"{Signal}%";

    public string SignalSymbol => Signal >= 80 ? "Wifi324" : Signal >= 55 ? "Wifi224" : "Wifi124";
}

/// <summary>Wi-Fi 扫描与一键切换(netsh wlan,中文/英文系统输出均可解析)。</summary>
public static class WlanService
{
    /// <summary>本机是否有 WLAN 接口。</summary>
    public static bool IsWlanAvailable()
    {
        var r = CommandRunner.Run("netsh", "wlan", "show", "interfaces");
        return r.Ok && !r.Output.Contains("没有运行 WLAN 服务", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>列出本机已保存的 Wi-Fi 配置文件名(中英文系统输出均可解析)。</summary>
    public static List<string> ListSavedProfiles()
    {
        var r = CommandRunner.Run("netsh", "wlan", "show", "profiles");
        var list = new List<string>();
        if (!r.Ok) return list;
        foreach (var line in r.Output.Split('\n'))
        {
            // "所有用户配置文件 : MyWifi" / "All User Profile : MyWifi"
            var m = Regex.Match(line, @"^\s*(?:所有用户配置文件|All User Profile)\s*:\s*(.+?)\s*$");
            if (m.Success) list.Add(m.Groups[1].Value);
        }
        return list;
    }

    /// <summary>读取已保存配置文件的明文密码:开放网络返回空串,配置不存在/读取失败返回 null。</summary>
    public static string? GetSavedPassword(string ssid)
    {
        var r = CommandRunner.Run("netsh", "wlan", "show", "profile", $"name={ssid}", "key=clear");
        if (!r.Ok) return null;
        foreach (var line in r.Output.Split('\n'))
        {
            // "关键内容 : password" / "Key Content : password"
            var m = Regex.Match(line, @"^\s*(?:关键内容|Key Content)\s*:\s*(.*?)\s*$");
            if (m.Success) return m.Groups[1].Value;
        }
        return ""; // 配置文件存在但没有密码字段(开放网络)
    }

    /// <summary>当前已连接的 SSID(未连 Wi-Fi 返回 null)。</summary>
    public static string? GetCurrentSsid()
    {
        var r = CommandRunner.Run("netsh", "wlan", "show", "interfaces");
        if (!r.Ok) return null;
        foreach (var line in r.Output.Split('\n'))
        {
            // "    SSID                   : MyWifi"(BSSID 行因带数字不会匹配)
            var m = Regex.Match(line, @"^\s*SSID\s+\d*\s*:\s*(.+?)\s*$");
            if (m.Success && m.Groups[1].Value.Length > 0)
                return m.Groups[1].Value;
        }
        return null;
    }

    /// <summary>列出附近可见的 Wi-Fi 网络。</summary>
    public static List<WlanNetwork> ListNetworks(string? currentSsid)
    {
        var r = CommandRunner.Run("netsh", "wlan", "show", "networks", "mode=bssid");
        var list = new List<WlanNetwork>();
        if (!r.Ok) return list;
        Log.Info($"wlan scan: exit={r.ExitCode} outLen={r.Output.Length} sample={Truncate(r.Output.Replace("\r", "").Replace("\n", "⏎"), 260)}");

        string? ssid = null, auth = null;
        var signal = 0;
        foreach (var raw in r.Output.Split('\n'))
        {
            var line = raw.TrimEnd();

            var m = Regex.Match(line, @"^SSID\s+\d+\s*:\s*(.*)$");
            if (m.Success)
            {
                Push(list, ssid, auth, signal, currentSsid);
                ssid = m.Groups[1].Value.Trim();
                auth = null;
                signal = 0;
                continue;
            }

            m = Regex.Match(line, @"^\s*(?:身份验证|认证|Authentication)\s*:\s*(.+?)\s*$");
            if (m.Success) { auth = m.Groups[1].Value.Trim(); continue; }

            m = Regex.Match(line, @"^\s*(?:信号|Signal)\s*:\s*(\d+)%");
            if (m.Success) { signal = Math.Max(signal, int.Parse(m.Groups[1].Value)); continue; }
        }
        Push(list, ssid, auth, signal, currentSsid);

        return list.OrderByDescending(n => n.Connected)
                   .ThenByDescending(n => n.Signal)
                   .ToList();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static void Push(List<WlanNetwork> list, string? ssid, string? auth, int signal, string? currentSsid)
    {
        if (string.IsNullOrEmpty(ssid)) return; // 隐藏网络(空 SSID)跳过
        list.Add(new WlanNetwork(ssid, auth ?? "", signal,
            currentSsid != null && ssid.Equals(currentSsid, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>开放网络(无密码)。首次连接也需要先写入配置文件才能 netsh connect。</summary>
    public static bool IsOpenNetwork(string auth) =>
        auth.Contains("开放", StringComparison.OrdinalIgnoreCase) || auth.Contains("Open", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 一键切换连接。password 为 null/空时直接用已保存的配置连接;
    /// 提供密码时会生成/覆盖该网络的配置文件再连接(开放网络无需密码也会先写配置)。
    /// </summary>
    public static async Task<(bool ok, string message)> ConnectAsync(string ssid, string? password, string auth = "")
    {
        if (!string.IsNullOrEmpty(password) || IsOpenNetwork(auth))
        {
            var xmlPath = await WriteProfileAsync(ssid, password ?? "", auth).ConfigureAwait(false);
            var add = await CommandRunner.RunAsync("netsh", "wlan", "add", "profile",
                $"filename={xmlPath}", "user=all").ConfigureAwait(false);
            try { File.Delete(xmlPath); } catch { }
            if (!add.Ok)
                return (false, "写入 Wi-Fi 配置失败:" + add.AllText);
        }

        var connect = await CommandRunner.RunAsync("netsh", "wlan", "connect",
            $"name={ssid}").ConfigureAwait(false);
        return connect.Ok
            ? (true, $"正在连接「{ssid}」…")
            : (false, "连接请求失败:" + connect.AllText + "(若该网络未保存过密码,请提供密码重试)");
    }

    /// <summary>等待连接结果(轮询当前 SSID,最多 12 秒)。</summary>
    public static async Task<bool> WaitConnectedAsync(string ssid, int timeoutSec = 12)
    {
        for (var i = 0; i < timeoutSec; i++)
        {
            await Task.Delay(1000).ConfigureAwait(false);
            var cur = GetCurrentSsid();
            if (cur != null && cur.Equals(ssid, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static async Task<string> WriteProfileAsync(string ssid, string password, string auth)
    {
        string authentication, encryption;
        if (auth.Contains("过渡", StringComparison.OrdinalIgnoreCase) || auth.Contains("Transition", StringComparison.OrdinalIgnoreCase))
        { authentication = "WPA2PSK"; encryption = "AES"; } // WPA2/WPA3 过渡模式用 WPA2 兼容性最好
        else if (auth.Contains("WPA3", StringComparison.OrdinalIgnoreCase))
        { authentication = "WPA3SAE"; encryption = "AES"; }
        else if (auth.Contains("WEP", StringComparison.OrdinalIgnoreCase))
        { authentication = "open"; encryption = "WEP"; }
        else if (auth.Contains("开放", StringComparison.OrdinalIgnoreCase) || auth.Contains("Open", StringComparison.OrdinalIgnoreCase))
        { authentication = "open"; encryption = "none"; }
        else
        { authentication = "WPA2PSK"; encryption = "AES"; } // WPA2-个人/企业等默认按 PSK 处理

        var open = authentication == "open" && encryption == "none";
        var keySection = open
            ? ""
            : $"<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{EscapeXml(password)}</keyMaterial></sharedKey>";

        var xml = $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
  <name>{EscapeXml(ssid)}</name>
  <SSIDConfig><SSID><name>{EscapeXml(ssid)}</name></SSID></SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>auto</connectionMode>
  <MSM><security>
    <authEncryption><authentication>{authentication}</authentication><encryption>{encryption}</encryption><useOneX>false</useOneX></authEncryption>
    {keySection}
  </security></MSM>
</WLANProfile>";

        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EasyWin");
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, $"wlan-{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(path, xml, Encoding.UTF8).ConfigureAwait(false);
        return path;
    }

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
