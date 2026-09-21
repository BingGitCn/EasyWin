using System.IO;
using System.Text;

namespace EasyWin.Services;

/// <summary>极简文件日志,永不抛出异常。按天建文件,保留最近 30 天。</summary>
public static class Log
{
    private const int RetentionDays = 30;
    private static readonly object _lock = new();
    private static bool _cleaned;

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (_lock)
            {
                var file = Path.Combine(AppPaths.LogsDir, $"easywin-{DateTime.Now:yyyyMMdd}.log");
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
                if (ex != null) line += $"\r\n    {ex.GetType().Name}: {ex.Message}\r\n    {ex.StackTrace}";
                File.AppendAllText(file, line + Environment.NewLine, new UTF8Encoding(false));
                if (!_cleaned)
                {
                    _cleaned = true;
                    CleanupOldLogs();
                }
            }
        }
        catch
        {
            // 日志失败静默
        }
    }

    private static void CleanupOldLogs()
    {
        foreach (var old in Directory.GetFiles(AppPaths.LogsDir, "easywin-*.log"))
        {
            if (File.GetLastWriteTime(old) < DateTime.Now.AddDays(-RetentionDays))
                File.Delete(old);
        }
    }
}
