using System.IO;

namespace EasyWin.Services;

/// <summary>数据目录、日志、配置文件的统一路径管理,统一存放于 %AppData%\EasyWin。</summary>
public static class AppPaths
{
    private static string? _dataDir;

    public static string ExeDir => AppContext.BaseDirectory;

    /// <summary>数据目录:%AppData%\EasyWin(首次访问时自动迁移旧版 exe 目录下的数据)。</summary>
    public static string DataDir
    {
        get
        {
            if (_dataDir != null) return _dataDir;

            _dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyWin");
            Directory.CreateDirectory(_dataDir);
            MigrateLegacyData();
            return _dataDir;
        }
    }

    public static string LogsDir
    {
        get
        {
            var dir = Path.Combine(DataDir, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ProfilesPath => Path.Combine(DataDir, "profiles.json");

    public static string RdpProfilesPath => Path.Combine(DataDir, "rdp.json");

    /// <summary>把旧版(exe 目录便携模式)遗留的数据文件迁移到用户目录,只迁移新位置不存在的文件。</summary>
    private static void MigrateLegacyData()
    {
        try
        {
            foreach (var name in new[] { "profiles.json", "rdp.json", "settings.json" })
            {
                var legacy = Path.Combine(ExeDir, name);
                var target = Path.Combine(_dataDir!, name);
                if (File.Exists(legacy) && !File.Exists(target))
                {
                    File.Copy(legacy, target);
                    Log.Info($"已迁移旧数据 {name} → {target}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("迁移旧数据失败: " + ex.Message);
        }
    }
}
