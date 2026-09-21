namespace EasyWin.Models;

public record PowerPlan(string Guid, string Name, bool IsActive);

/// <summary>端口占用条目(解析 netstat -ano 所得)。</summary>
public class PortUsage
{
    public string Protocol { get; set; } = "TCP";

    /// <summary>本地端点,如 0.0.0.0:8080 或 [::]:8080。</summary>
    public string Local { get; set; } = "";

    public string Remote { get; set; } = "";

    public string State { get; set; } = "";

    public int ProcessId { get; set; }

    public string ProcessName { get; set; } = "";

    public string ProcessPath { get; set; } = "";

    public string Summary => State == "无连接状态"
        ? $"{Protocol} {Local}"
        : $"{Protocol} {Local} ← {Remote} ({State})";
}

public record DiskInfo(string Drive, string Label, long TotalBytes, long FreeBytes, bool IsRemovable = false)
{
    public string TotalText => Format(TotalBytes);
    public string FreeText => Format(FreeBytes);

    public string UsageText
    {
        get
        {
            if (TotalBytes <= 0) return "—";
            return $"已用 {Format(TotalBytes - FreeBytes)} / {Format(TotalBytes)}";
        }
    }

    /// <summary>0-100,用于进度条。</summary>
    public int UsagePercent => TotalBytes <= 0 ? 0 : (int)Math.Round((TotalBytes - FreeBytes) * 100.0 / TotalBytes);

    private static string Format(long bytes) => bytes switch
    {
        >= 1L << 40 => $"{bytes / (double)(1L << 40):0.#} TB",
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        _ => $"{bytes} B"
    };
}
