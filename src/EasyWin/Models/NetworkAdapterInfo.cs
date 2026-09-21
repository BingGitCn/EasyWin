using System.Net.NetworkInformation;

namespace EasyWin.Models;

/// <summary>网卡信息(来自 WMI + NetworkInformation 合并),对应 ncpa.cpl 里的连接。</summary>
public class NetworkAdapterInfo
{
    /// <summary>连接名,即 netsh 操作所用的名称(如"以太网""WLAN")。</summary>
    public string ConnectionName { get; set; } = "";

    public string Description { get; set; } = "";

    public string MacAddress { get; set; } = "";

    /// <summary>类型描述:以太网 / WLAN / 蓝牙 / 虚拟 等。</summary>
    public string AdapterType { get; set; } = "";

    /// <summary>管理员启用状态(WMI NetEnabled)。禁用的网卡不在 NetworkInterface 列表里。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>链路已连接(OperationalStatus == Up)。</summary>
    public bool IsConnected { get; set; }

    public bool IsWifi { get; set; }

    /// <summary>接口速率(bits/s),禁用或未知时为 null。</summary>
    public long? Speed { get; set; }

    public bool DhcpEnabled { get; set; }

    public string? IpAddress { get; set; }

    public string? SubnetMask { get; set; }

    public string? Gateway { get; set; }

    /// <summary>IPv4 DNS,逗号分隔展示。</summary>
    public string? DnsServers { get; set; }

    public string StatusText => !IsEnabled ? "已禁用" : IsConnected ? "已连接" : "未连接";

    public string SpeedText => Speed switch
    {
        null or <= 0 => "—",
        >= 1_000_000_000 => $"{Speed / 1_000_000_000.0:0.#} Gbps",
        >= 1_000_000 => $"{Speed / 1_000_000.0:0} Mbps",
        _ => $"{Speed / 1_000.0:0} Kbps"
    };

    /// <summary>列表里的小圆点颜色:绿=已连接 黄=未连接 灰=已禁用。</summary>
    public string StatusColor => !IsEnabled ? "#8A8A8A" : IsConnected ? "#5CB87A" : "#E2B93B";

    public string TypeSymbol => IsWifi ? "Wifi124" : "Globe24";

    /// <summary>参数摘要,用于方案卡片与下拉提示。</summary>
    public string IpSummary => DhcpEnabled
        ? $"DHCP · {IpAddress ?? "未获取"}"
        : $"静态 · {IpAddress ?? "—"}";

    /// <summary>列表页的摘要行。</summary>
    public string ListSummary => $"{StatusText} · {(string.IsNullOrEmpty(IpAddress) ? "无 IP" : IpAddress)}";

    public string DhcpText => DhcpEnabled ? "DHCP 自动" : "手动指定";

    /// <summary>如 "192.168.1.100/24";无 IP 时为 "—"。</summary>
    public string IpPrefixText => string.IsNullOrEmpty(IpAddress) ? "—" : $"{IpAddress}/{Prefix}";

    public int Prefix => SubnetMask is null ? 0 : NetworkPrefix.PrefixFromMask(SubnetMask);
}

/// <summary>掩码/前缀换算(独立静态类,避免循环依赖)。</summary>
public static class NetworkPrefix
{
    public static int PrefixFromMask(string? mask)
    {
        if (mask is null || System.Net.IPAddress.TryParse(mask.Trim(), out var addr) == false)
            return 0;
        var bytes = addr.GetAddressBytes();
        if (bytes.Length != 4) return 0;
        int prefix = 0;
        foreach (var b in bytes)
            prefix += PopCount(b);
        return prefix;
    }

    private static int PopCount(byte b)
    {
        int count = 0;
        while (b != 0) { count += b & 1; b >>= 1; }
        return count;
    }
}
