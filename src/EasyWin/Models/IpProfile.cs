namespace EasyWin.Models;

using System.Text.Json.Serialization;

public enum IpConfigMode
{
    Dhcp,
    Static
}

/// <summary>IP 配置方案:绑定一块网卡的一套 IP 参数,可一键应用。</summary>
public class IpProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>目标网卡的连接名(netsh 名称)。</summary>
    public string AdapterName { get; set; } = "";

    public IpConfigMode Mode { get; set; } = IpConfigMode.Static;

    public string IpAddress { get; set; } = "";

    public string SubnetMask { get; set; } = "";

    public string Gateway { get; set; } = "";

    public string Dns1 { get; set; } = "";

    public string Dns2 { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>运行态:方案是否与网卡当前配置一致,由列表页刷新时计算,不持久化。</summary>
    [JsonIgnore]
    public bool InUse { get; set; }

    public string ModeText => Mode == IpConfigMode.Dhcp ? "DHCP 自动获取" : "静态 IP";

    /// <summary>参数摘要,展示在方案卡片上。DHCP 与静态都用四行结构,保证卡片信息块等高。</summary>
    public string Summary => Mode == IpConfigMode.Dhcp
        ? "IP 自动获取\n掩码 自动获取\n网关 自动获取\nDNS 自动获取"
        : $"IP {IpAddress}\n掩码 {SubnetMask}\n网关 {(string.IsNullOrWhiteSpace(Gateway) ? "无" : Gateway)}\nDNS {JoinDns()}";

    private string JoinDns()
    {
        var dns = string.IsNullOrWhiteSpace(Dns1) ? "自动" : Dns1;
        if (!string.IsNullOrWhiteSpace(Dns2)) dns += " / " + Dns2;
        return dns;
    }
}
