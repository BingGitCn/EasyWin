using System.IO;

namespace EasyWin.Services;

/// <summary>数据目录、日志、配置文件的统一路径管理。优先便携模式(exe 目录),不可写时回退 %AppData%。</summary>
public static class AppPaths
{
    private static string? _dataDir;

    public static string ExeDir => AppContext.BaseDirectory;

    /// <summary>数据目录:exe 目录可写则用 exe 目录(便携),否则 %AppData%\EasyWin。</summary>
    public static string DataDir
    {
        get
        {
            if (_dataDir != null) return _dataDir;

            try
            {
                var probe = Path.Combine(ExeDir, ".write-test");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                _dataDir = ExeDir;
            }
            catch
            {
                _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyWin");
                Directory.CreateDirectory(_dataDir);
            }
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
}
