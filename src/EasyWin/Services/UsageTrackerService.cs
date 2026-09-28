using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace EasyWin.Services;

/// <summary>单条使用统计:进程名 → 时长(小时)与占比。</summary>
public record UsageStatEntry(string Process, double Hours, double Percent);

/// <summary>一段时间内的汇总统计。</summary>
public record UsageStats(double TotalHours, int Days, List<UsageStatEntry> Entries);

/// <summary>
/// 软件使用时长统计(轻量版,理念参考 Planshit/Tai,MIT):托盘常驻期间每 15 秒采样前台窗口进程,
/// 把间隔计入该进程;键盘鼠标无输入超过 5 分钟视为离开不计。数据仅存本地 usage.json,保留 30 天。
/// </summary>
public static class UsageTrackerService
{
    private const int SampleIntervalMs = 15_000;
    private const int AwayAfterMs = 5 * 60_000;
    private const int KeepDays = 30;
    private const int FlushIntervalSec = 120;

    private static readonly object _lock = new();
    private static System.Threading.Timer? _timer;
    private static DateTime _lastTick = DateTime.Now;
    private static string _lastProcess = "";
    private static DateTime _lastFlush = DateTime.Now;

    private static string Path => System.IO.Path.Combine(AppPaths.DataDir, "usage.json");
    private static Dictionary<string, Dictionary<string, double>> _days = Load();

    private static string TodayKey => DateTime.Now.ToString("yyyy-MM-dd");

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    /// <summary>按设置启动或停止后台统计;应用启动时调用。</summary>
    public static void Initialize()
    {
        if (SettingsStore.UsageTrackingEnabled) Start();
    }

    public static void SetEnabled(bool enabled)
    {
        SettingsStore.UsageTrackingEnabled = enabled;
        if (enabled) Start();
        else Stop();
    }

    public static bool IsRunning => _timer != null;

    private static void Start()
    {
        lock (_lock)
        {
            if (_timer != null) return;
            _lastTick = DateTime.Now;
            _lastProcess = GetForegroundProcessName();
            _timer = new System.Threading.Timer(_ => Tick(), null, SampleIntervalMs, SampleIntervalMs);
            Log.Info("软件使用统计已开启");
        }
    }

    private static void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            Flush();
            Log.Info("软件使用统计已关闭");
        }
    }

    /// <summary>一个采样周期:把上一个间隔记到当时的进程上,再采样新的前台进程。</summary>
    private static void Tick()
    {
        try
        {
            var now = DateTime.Now;

            // 离开检测:无输入超过阈值则整段不计,但仍推进 _lastTick 避免把离开时间补记给进程
            if (IdleMilliseconds() < AwayAfterMs && _lastProcess.Length > 0)
                AddSeconds(_lastProcess, (now - _lastTick).TotalSeconds);

            _lastTick = now;
            _lastProcess = GetForegroundProcessName();

            if ((now - _lastFlush).TotalSeconds >= FlushIntervalSec)
                Flush();
        }
        catch (Exception ex)
        {
            Log.Warn("使用统计采样失败: " + ex.Message);
        }
    }

    private static double IdleMilliseconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info)
            ? Math.Max(0, Environment.TickCount - (long)info.dwTime)
            : 0;
    }

    private static string GetForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return ""; // 锁屏/安全桌面
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return "";
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return ""; // 进程已退出或无权限,丢弃该次采样
        }
    }

    // ---------------- 存储 ----------------

    private static void AddSeconds(string process, double seconds)
    {
        lock (_lock)
        {
            if (seconds <= 0 || seconds > SampleIntervalMs * 4 / 1000.0 + 60) return; // 休眠唤醒后的巨长间隔丢弃
            var key = TodayKey;
            if (!_days.TryGetValue(key, out var day))
            {
                day = [];
                _days[key] = day;
            }
            day[process] = day.GetValueOrDefault(process) + seconds;
        }
    }

    private static Dictionary<string, Dictionary<string, double>> Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(
                    File.ReadAllText(Path));
                if (data != null) return data;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("读取使用统计失败: " + ex.Message);
        }
        return [];
    }

    private static void Flush()
    {
        lock (_lock)
        {
            _lastFlush = DateTime.Now;
            try
            {
                // 只保留最近 30 天
                var cutoff = DateTime.Now.AddDays(-KeepDays).ToString("yyyy-MM-dd");
                foreach (var stale in _days.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList())
                    _days.Remove(stale);

                File.WriteAllText(Path, JsonSerializer.Serialize(_days, new JsonSerializerOptions { WriteIndented = false }));
            }
            catch (Exception ex)
            {
                Log.Warn("保存使用统计失败: " + ex.Message);
            }
        }
    }

    /// <summary>汇总最近 N 天的使用统计(按时长降序,Process 为空字符串的采样丢弃)。</summary>
    public static UsageStats GetStats(int days)
    {
        lock (_lock)
        {
            Flush();
            var cutoff = DateTime.Now.AddDays(-(days - 1)).ToString("yyyy-MM-dd");
            var totals = new Dictionary<string, double>();
            var dayCount = 0;
            foreach (var (key, day) in _days)
            {
                if (string.CompareOrdinal(key, cutoff) < 0) continue;
                dayCount++;
                foreach (var (process, seconds) in day)
                {
                    if (process.Length == 0 || seconds <= 0) continue;
                    totals[process] = totals.GetValueOrDefault(process) + seconds;
                }
            }

            var totalSeconds = totals.Values.Sum();
            var entries = totals
                .OrderByDescending(kv => kv.Value)
                .Select(kv => new UsageStatEntry(
                    kv.Key,
                    kv.Value / 3600.0,
                    totalSeconds > 0 ? kv.Value / totalSeconds * 100 : 0))
                .ToList();
            return new UsageStats(totalSeconds / 3600.0, dayCount, entries);
        }
    }
}
