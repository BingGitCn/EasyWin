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
        CleanupOldBackups();
        Log.Info($"hosts 已保存(备份: {backup})");
        return backup;
    }

    /// <summary>备份只保留最近 20 份,避免无限累积。</summary>
    private static void CleanupOldBackups()
    {
        try
        {
            var dir = Path.GetDirectoryName(HostsPath)!;
            var backups = Directory.GetFiles(dir, "hosts.easywin-*.bak")
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();
            foreach (var old in backups.Skip(20))
                File.Delete(old);
        }
        catch (Exception ex)
        {
            Log.Warn("清理 hosts 备份失败: " + ex.Message);
        }
    }
}
