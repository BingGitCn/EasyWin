using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using EasyWin.Models;

namespace EasyWin.Services;

/// <summary>网卡枚举(含已禁用)与 IP/DNS/启停等网络操作,底层走 WMI 查询 + netsh 命令。</summary>
public class NetworkService
{
    /// <summary>列出所有有连接名的网卡(等价于 ncpa.cpl 里的列表),已连接的排前面。</summary>
    public List<NetworkAdapterInfo> GetAdapters()
    {
        // 1) WMI:全部适配器(含已禁用),以 NetConnectionID 有值为准
        var wmiAdapters = new Dictionary<int, ManagementBaseObject>();
        using (var searcher = new ManagementObjectSearcher(
            "SELECT Index, InterfaceIndex, NetConnectionID, Name, Description, NetEnabled, MACAddress, AdapterType, Speed " +
            "FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL"))
        {
            foreach (var o in searcher.Get())
                wmiAdapters[(int)(uint)o["Index"]] = o;
        }

        // 2) WMI:IP 配置(Index 与上表对应,已禁用的网卡也能取到配置)
        var configs = new Dictionary<int, ManagementBaseObject>();
        using (var searcher = new ManagementObjectSearcher(
            "SELECT Index, IPAddress, IPSubnet, DefaultIPGateway, DNSServerSearchOrder, DHCPEnabled " +
            "FROM Win32_NetworkAdapterConfiguration"))
        {
            foreach (var o in searcher.Get())
                configs[(int)(uint)o["Index"]] = o;
        }

        // 3) .NET:运行态(连接状态、速率、类型),按规范化 MAC 关联
        var byMac = new Dictionary<string, NetworkInterface>(StringComparer.OrdinalIgnoreCase);
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            var mac = ni.GetPhysicalAddress()?.ToString();
            if (!string.IsNullOrEmpty(mac))
                byMac.TryAdd(mac.ToLowerInvariant(), ni);
        }

        var result = new List<NetworkAdapterInfo>();
        foreach (var adapter in wmiAdapters.Values)
        {
            var info = new NetworkAdapterInfo
            {
                ConnectionName = (string)adapter["NetConnectionID"],
                Description = (string)(adapter["Description"] ?? ""),
                MacAddress = FormatMac((string?)adapter["MACAddress"]),
                AdapterType = MapType((string?)adapter["AdapterType"], null),
                IsEnabled = adapter["NetEnabled"] is bool enabled && enabled,
            };

            if (info.IsEnabled)
            {
                var runtime = byMac.GetValueOrDefault(NormalizeMac((string?)adapter["MACAddress"]));
                if (runtime != null)
                {
                    info.IsConnected = runtime.OperationalStatus == OperationalStatus.Up;
                    info.Speed = runtime.Speed > 0 ? runtime.Speed : null;
                    info.AdapterType = MapType((string?)adapter["AdapterType"], runtime.NetworkInterfaceType);
                }
            }

            if (configs.TryGetValue((int)(uint)adapter["Index"], out var cfg))
            {
                info.DhcpEnabled = cfg["DHCPEnabled"] is bool dhcp && dhcp;
                (info.IpAddress, info.SubnetMask) = ExtractV4AddressAndMask(cfg);
                info.Gateway = ExtractFirstV4(cfg["DefaultIPGateway"]);
                info.DnsServers = ExtractJoinedV4(cfg["DNSServerSearchOrder"]);
            }

            result.Add(info);
        }

        return result
            .OrderByDescending(a => a.IsConnected)
            .ThenByDescending(a => a.IsEnabled)
            .ThenBy(a => a.ConnectionName, StringComparer.CurrentCulture)
            .ToList();
    }

    // ---------- netsh 操作 ----------

    public async Task<CmdResult> SetStaticIpAsync(string connectionName, string ip, string mask, string? gateway)
    {
        var args = string.IsNullOrWhiteSpace(gateway)
            ? new[] { "interface", "ipv4", "set", "address", $"name={connectionName}", "source=static", $"addr={ip}", $"mask={mask}", "gateway=none" }
            : new[] { "interface", "ipv4", "set", "address", $"name={connectionName}", "source=static", $"addr={ip}", $"mask={mask}", $"gateway={gateway}", "gwmetric=1" };
        return await CommandRunner.RunAsync("netsh", args).ConfigureAwait(false);
    }

    public async Task<CmdResult> SetDhcpIpAsync(string connectionName) =>
        await CommandRunner.RunAsync("netsh", "interface", "ipv4", "set", "address",
            $"name={connectionName}", "source=dhcp").ConfigureAwait(false);

    public async Task<CmdResult> SetStaticDnsAsync(string connectionName, string? dns1, string? dns2)
    {
        if (string.IsNullOrWhiteSpace(dns1))
            return await SetDhcpDnsAsync(connectionName).ConfigureAwait(false);

        var primary = await CommandRunner.RunAsync("netsh", "interface", "ipv4", "set", "dnsservers",
            $"name={connectionName}", "source=static", $"address={dns1}", "register=primary", "validate=no").ConfigureAwait(false);
        if (!primary.Ok) return primary;

        if (!string.IsNullOrWhiteSpace(dns2))
            return await CommandRunner.RunAsync("netsh", "interface", "ipv4", "add", "dnsservers",
                $"name={connectionName}", $"address={dns2}", "index=2", "validate=no").ConfigureAwait(false);

        return primary;
    }

    public async Task<CmdResult> SetDhcpDnsAsync(string connectionName) =>
        await CommandRunner.RunAsync("netsh", "interface", "ipv4", "set", "dnsservers",
            $"name={connectionName}", "source=dhcp").ConfigureAwait(false);

    public async Task<CmdResult> SetAdapterStateAsync(string connectionName, bool enable) =>
        await CommandRunner.RunAsync("netsh", "interface", "set", "interface",
            $"name={connectionName}", enable ? "admin=enable" : "admin=disable").ConfigureAwait(false);

    /// <summary>对网卡应用一套完整配置(方案切换的核心):IP 模式 + DNS。</summary>
    public async Task<(bool ok, string message)> ApplyProfileAsync(IpProfile profile)
    {
        CmdResult addressResult = profile.Mode == IpConfigMode.Static
            ? await SetStaticIpAsync(profile.AdapterName, profile.IpAddress, profile.SubnetMask, profile.Gateway).ConfigureAwait(false)
            : await SetDhcpIpAsync(profile.AdapterName).ConfigureAwait(false);

        if (!addressResult.Ok)
            return (false, $"IP 设置失败:{addressResult.AllText}");

        CmdResult dnsResult = profile.Mode == IpConfigMode.Static
            ? await SetStaticDnsAsync(profile.AdapterName, profile.Dns1, profile.Dns2).ConfigureAwait(false)
            : await SetDhcpDnsAsync(profile.AdapterName).ConfigureAwait(false);

        return dnsResult.Ok
            ? (true, $"{profile.AdapterName} 已切换为「{profile.Name}」")
            : (false, $"IP 已生效,但 DNS 设置失败:{dnsResult.AllText}");
    }

    public async Task<PingReply?> PingAsync(string host, int timeoutMs = 2000)
    {
        try
        {
            using var ping = new Ping();
            return await ping.SendPingAsync(host, timeoutMs).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    // ---------- 工具方法 ----------

    public static bool IsValidIPv4(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!IPAddress.TryParse(text.Trim(), out var addr)) return false;
        return addr.AddressFamily == AddressFamily.InterNetwork;
    }

    public static bool IsValidMask(string? text)
    {
        if (!IsValidIPv4(text)) return false;
        // 掩码必须是连续的 1 后跟连续的 0
        var bytes = IPAddress.Parse(text!.Trim()).GetAddressBytes();
        bool hitZero = false;
        foreach (var b in bytes)
        {
            for (int i = 7; i >= 0; i--)
            {
                bool one = (b & (1 << i)) != 0;
                if (!one) hitZero = true;
                else if (hitZero) return false;
            }
        }
        return true;
    }

    public static int PrefixFromMask(string? mask)
    {
        if (!IsValidIPv4(mask)) return 24;
        int prefix = 0;
        foreach (var b in IPAddress.Parse(mask!.Trim()).GetAddressBytes())
            prefix += BitOperationsPopCount(b);
        return prefix;
    }

    public static string MaskFromPrefix(int prefix)
    {
        uint value = prefix == 0 ? 0 : 0xFFFFFFFF << (32 - Math.Clamp(prefix, 0, 32));
        var bytes = BitConverter.GetBytes(value);
        Array.Reverse(bytes);
        return new IPAddress(bytes).ToString();
    }

    private static int BitOperationsPopCount(byte b)
    {
        int count = 0;
        while (b != 0) { count += b & 1; b >>= 1; }
        return count;
    }

    private static string? ExtractFirstV4(object? wmiArray)
    {
        if (wmiArray is not string[] list) return null;
        foreach (var item in list)
            if (IsValidIPv4(item)) return item.Trim();
        return null;
    }

    private static string? ExtractJoinedV4(object? wmiArray)
    {
        if (wmiArray is not string[] list) return null;
        var v4 = list.Where(IsValidIPv4).Select(s => s.Trim()).ToList();
        return v4.Count == 0 ? null : string.Join(", ", v4);
    }

    private static (string? ip, string? mask) ExtractV4AddressAndMask(ManagementBaseObject cfg)
    {
        if (cfg["IPAddress"] is not string[] addresses) return (null, null);
        var masks = cfg["IPSubnet"] as string[];

        for (var i = 0; i < addresses.Length; i++)
        {
            if (!IsValidIPv4(addresses[i])) continue;
            string? mask = masks != null && i < masks.Length && IsValidIPv4(masks[i]) ? masks[i] : null;
            return (addresses[i].Trim(), mask);
        }
        return (null, null);
    }

    private static string NormalizeMac(string? mac) =>
        (mac ?? "").Replace(":", "").Replace("-", "").ToLowerInvariant();

    private static string FormatMac(string? mac)
    {
        var normalized = NormalizeMac(mac);
        if (normalized.Length != 12) return mac ?? "";
        return string.Join(":", Enumerable.Range(0, 6).Select(i => normalized.Substring(i * 2, 2)).ToArray()).ToUpperInvariant();
    }

    private static string MapType(string? wmiType, NetworkInterfaceType? runtimeType)
    {
        if (runtimeType == NetworkInterfaceType.Wireless80211) return "WLAN";
        if (runtimeType == NetworkInterfaceType.Ethernet && wmiType is null) return "以太网";

        var t = wmiType ?? "";
        if (t.Contains("802.11", StringComparison.OrdinalIgnoreCase) || t.Contains("Wireless", StringComparison.OrdinalIgnoreCase)) return "WLAN";
        if (t.Contains("Ethernet", StringComparison.OrdinalIgnoreCase)) return "以太网";
        if (t.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)) return "蓝牙";
        if (t.Contains("FireWire", StringComparison.OrdinalIgnoreCase)) return "FireWire";
        if (t.Contains("Tunnel", StringComparison.OrdinalIgnoreCase)) return "隧道";
        if (runtimeType == NetworkInterfaceType.Loopback) return "环回";

        // 虚拟网卡常见前缀
        return runtimeType switch
        {
            NetworkInterfaceType.Ethernet => "以太网",
            NetworkInterfaceType.Ethernet3Megabit => "以太网",
            NetworkInterfaceType.FastEthernetT => "以太网",
            NetworkInterfaceType.GigabitEthernet => "以太网",
            _ => string.IsNullOrWhiteSpace(t) ? "其他" : t.Length > 16 ? "虚拟适配器" : t
        };
    }
}
