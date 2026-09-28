using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace EasyWin.Services;

/// <summary>单条使用统计:进程显示名 → 时长(小时)与占比。</summary>
public record UsageStatEntry(string Process, double Hours, double Percent);

/// <summary>一段时间内的汇总统计。</summary>
public record UsageStats(double TotalHours, int Days, List<UsageStatEntry> Entries);

/// <summary>
/// 软件使用时长统计(轻量版,理念参考 Planshit/Tai,MIT):托盘常驻期间每 15 秒采样前台窗口进程,
/// 把间隔计入该进程;键盘鼠标无输入超过 5 分钟视为离开不计。数据仅存本地 usage.json,保留 30 天。
/// 展示用友好名:采样时读 exe 的 FileDescription(如 devenv → Visual Studio 2026),内置字典兜底。
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
    private static Dictionary<string, Dictionary<string, double>> _days = [];
    private static Dictionary<string, string> _names = [];

    private static string TodayKey => DateTime.Now.ToString("yyyy-MM-dd");

    static UsageTrackerService() => Load();

    /// <summary>常见进程的中文显示名兜底(exe 没写 FileDescription 或拿不到路径时用)。</summary>
    private static readonly Dictionary<string, string> BuiltinNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["explorer"] = "文件资源管理器",
        ["cmd"] = "命令提示符",
        ["pwsh"] = "PowerShell",
        ["conhost"] = "控制台窗口宿主",
        ["code"] = "VS Code",
        ["notepad"] = "记事本",
        ["mspaint"] = "画图",
        ["taskmgr"] = "任务管理器",
        ["ApplicationFrameHost"] = "UWP 应用宿主",
        ["SystemSettings"] = "系统设置",
        ["idea64"] = "IntelliJ IDEA",
        ["pycharm64"] = "PyCharm",
        ["webstorm64"] = "WebStorm",
        ["goland64"] = "GoLand",
        ["rider64"] = "Rider",
        ["studio64"] = "Android Studio",
        ["ssms"] = "SSMS",
        ["devenv"] = "Visual Studio",
    };

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
            _lastProcess = SampleForeground().Name;
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
            _lastProcess = SampleForeground().Name;

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

    private record ForegroundSample(string Name);

    /// <summary>取当前前台窗口的进程名;顺便把该进程的友好名登记进名字表(此刻进程一定在运行,能读到 exe 信息)。</summary>
    private static ForegroundSample SampleForeground()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return new ForegroundSample(""); // 锁屏/安全桌面
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return new ForegroundSample("");
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            if (name.Length > 0 && !_names.ContainsKey(name))
            {
                string? exePath = null;
                try { exePath = process.MainModule?.FileName; } catch { /* 系统进程可能拒绝读取 */ }
                _names[name] = ResolveDisplayName(name, exePath);
            }
            return new ForegroundSample(name);
        }
        catch
        {
            return new ForegroundSample(""); // 进程已退出或无权限,丢弃该次采样
        }
    }

    /// <summary>友好名解析:内置字典 → exe 的 FileDescription(如 devenv → "Visual Studio 2026")→ 原进程名。</summary>
    private static string ResolveDisplayName(string raw, string? exePath)
    {
        if (BuiltinNames.TryGetValue(raw, out var builtin)) return builtin;
        if (!string.IsNullOrEmpty(exePath))
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                var description = info.FileDescription?.Trim();
                if (!string.IsNullOrWhiteSpace(description)) return description;
            }
            catch { /* 无版本信息/路径失效 */ }
        }
        return raw;
    }

    /// <summary>展示时懒解析:历史数据里没登记过友好名的,趁进程在运行再试一次。</summary>
    private static string DisplayNameOf(string raw)
    {
        if (_names.TryGetValue(raw, out var name)) return name;
        if (BuiltinNames.TryGetValue(raw, out var builtin)) return _names[raw] = builtin;
        try
        {
            var process = System.Diagnostics.Process.GetProcessesByName(raw).FirstOrDefault();
            var exePath = process?.MainModule?.FileName;
            return _names[raw] = ResolveDisplayName(raw, exePath);
        }
        catch
        {
            return _names[raw] = raw;
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

    private sealed class StorageData
    {
        public Dictionary<string, Dictionary<string, double>> Days { get; set; } = [];
        public Dictionary<string, string> Names { get; set; } = [];
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(Path)) return;
            var json = File.ReadAllText(Path);
            // 兼容旧格式(纯 days 字典)与新格式 {days, names}
            if (JsonSerializer.Deserialize<StorageData>(json) is { } wrapped && wrapped.Days.Count > 0)
            {
                _days = wrapped.Days;
                _names = wrapped.Names;
            }
            else if (JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(json) is { } legacy)
            {
                _days = legacy;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("读取使用统计失败: " + ex.Message);
        }
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

                File.WriteAllText(Path, JsonSerializer.Serialize(new StorageData { Days = _days, Names = _names }));
            }
            catch (Exception ex)
            {
                Log.Warn("保存使用统计失败: " + ex.Message);
            }
        }
    }

    /// <summary>汇总最近 N 天的使用统计(按时长降序;按友好名聚合,老数据展示时懒解析补登记)。</summary>
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
                    var display = DisplayNameOf(process);
                    totals[display] = totals.GetValueOrDefault(display) + seconds;
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

