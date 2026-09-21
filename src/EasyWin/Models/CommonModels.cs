namespace EasyWin.Models;

public record PowerPlan(string Guid, string Name, bool IsActive);

public record DiskInfo(string Drive, string Label, long TotalBytes, long FreeBytes)
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
