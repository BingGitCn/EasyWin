namespace EasyWin.Models;

/// <summary>代理方案:一组常用的系统代理配置,可一键切换。</summary>
public class ProxyProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>代理服务器,格式 地址:端口。</summary>
    public string Server { get; set; } = "";

    /// <summary>例外列表(分号分隔),与系统代理的 ProxyOverride 一致。</summary>
    public string Override { get; set; } = "";

    public string Summary => string.IsNullOrWhiteSpace(Override)
        ? Server
        : $"{Server}(例外:{Override})";
}
