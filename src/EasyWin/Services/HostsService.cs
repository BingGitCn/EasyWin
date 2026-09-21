using System.IO;
using System.Text;

namespace EasyWin.Services;

/// <summary>hosts 文件读写,保存前自动备份。</summary>
public static class HostsService
{
    public static string HostsPath => Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");

    public static string Read() => File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : "";

    public static string Save(string content)
    {
        var backup = "";
        if (File.Exists(HostsPath))
        {
            backup = HostsPath + $".easywin-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            File.Copy(HostsPath, backup, overwrite: true);
        }
        File.WriteAllText(HostsPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Log.Info($"hosts 已保存(备份: {backup})");
        return backup;
    }
}
