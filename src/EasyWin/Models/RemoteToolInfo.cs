using System.Windows.Media;

namespace EasyWin.Models;

/// <summary>第三方远程工具(向日葵/ToDesk 等)在卡片上的展示信息。</summary>
public class RemoteToolInfo
{
    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>官网地址(未安装时去下载)。</summary>
    public string Url { get; set; } = "";

    /// <summary>是否检测到本机安装。</summary>
    public bool Installed { get; set; }

    /// <summary>主程序完整路径(检测到时才有)。</summary>
    public string? ExePath { get; set; }

    /// <summary>从主程序提取的图标(提取失败为 null,卡片回退到占位符号)。</summary>
    public ImageSource? Icon { get; set; }
}
