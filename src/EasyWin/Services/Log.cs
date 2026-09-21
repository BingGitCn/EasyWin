using System.IO;
using System.Text;

namespace EasyWin.Services;

/// <summary>极简文件日志,永不抛出异常。</summary>
public static class Log
{
    private static readonly object _lock = new();

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var file = Path.Combine(AppPaths.LogsDir, $"easywin-{DateTime.Now:yyyyMMdd}.log");
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
            if (ex != null) line += $"\r\n    {ex.GetType().Name}: {ex.Message}\r\n    {ex.StackTrace}";
            lock (_lock)
                File.AppendAllText(file, line + Environment.NewLine, new UTF8Encoding(false));
        }
        catch
        {
            // 日志失败静默
        }
    }
}
