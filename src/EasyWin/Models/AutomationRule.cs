using System.Text.Json.Serialization;

namespace EasyWin.Models;

/// <summary>自动化规则:连接到指定 Wi-Fi 时,自动应用某个 IP 方案。</summary>
public class AutomationRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>触发条件的 Wi-Fi 名称(SSID)。</summary>
    public string Ssid { get; set; } = "";

    /// <summary>命中后要应用的 IP 方案。</summary>
    public Guid ProfileId { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>运行态:方案名称,列表刷新时解析,不持久化。</summary>
    [JsonIgnore]
    public string ProfileName { get; set; } = "";

    public string DisplayText =>
        $"连接「{Ssid}」时,自动应用「{(string.IsNullOrEmpty(ProfileName) ? "(方案已删除)" : ProfileName)}」";
}
