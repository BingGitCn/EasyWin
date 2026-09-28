using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace EasyWin.Services;

/// <summary>单个清理目标的扫描结果。Bytes=0 且 Files>0 表示以「项数」计(如注册表记录)。</summary>
public record CleanupScanResult(long Bytes, int Files, string? Note = null);

/// <summary>单个清理目标的执行结果。</summary>
public record CleanupResult(long FreedBytes, int Files, string? Note = null);

/// <summary>清理目标:只动明确列出的路径与模式,勾选了才清理(理念参考 builtbybel/FluentCleaner,MIT)。</summary>
public record CleanupTarget(
    string Id, string Name, string Description, string Symbol,
    Func<CleanupScanResult> Scan,
    Func<CleanupResult> Clean);

/// <summary>
/// 系统清理引擎:内置固定清理目标(临时文件、更新缓存、浏览器缓存、回收站、最近记录等)。
/// 与 FluentCleaner 一致不做注册表「深度清理」;被占用/受保护的文件一律跳过而非强删。
/// </summary>
public static class CleanerService
{
    private static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string CommonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    /// <summary>临时目录只动 24 小时前的旧文件,避免误删正在使用的文件(与 App 启动清理自身临时目录的约定一致)。</summary>
    private static readonly TimeSpan TempMinAge = TimeSpan.FromHours(24);

    public static List<CleanupTarget> Targets { get; } =
    [
        new("sys-temp", "系统临时文件夹",
            @"C:\Windows\Temp 下的临时文件(仅清理 24 小时前的旧文件)。",
            "Folder24",
            () => ScanDirs([(@"Windows\Temp", "*")], absoluteBase: WinDir, minAge: TempMinAge),
            () => CleanDirs([(@"Windows\Temp", "*")], absoluteBase: WinDir, minAge: TempMinAge)),

        new("user-temp", "用户临时文件夹",
            @"%TEMP%(通常在 %LOCALAPPDATA%\Temp),仅清理 24 小时前的旧文件。",
            "FolderOpen24",
            () => ScanDirs([(".", "*")], absoluteBase: Environment.GetEnvironmentVariable("TEMP") ?? "", minAge: TempMinAge),
            () => CleanDirs([(".", "*")], absoluteBase: Environment.GetEnvironmentVariable("TEMP") ?? "", minAge: TempMinAge)),

        new("update-cache", "Windows 更新缓存",
            "已安装更新留下的下载缓存(SoftwareDistribution\\Download)。更新正在下载时清了会重新下载。",
            "ArrowClockwise24",
            () => ScanDirs([("SoftwareDistribution\\Download", "*")], absoluteBase: WinDir),
            () => CleanDirs([("SoftwareDistribution\\Download", "*")], absoluteBase: WinDir)),

        new("delivery-opt", "传递优化缓存",
            "P2P 分发(含应用商店更新)的缓存文件。",
            "CloudArrowDown24",
            () => ScanDirs([(@"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache", "*")], absoluteBase: WinDir),
            () => CleanDirs([(@"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache", "*")], absoluteBase: WinDir)),

        new("thumb-cache", "缩略图与图标缓存",
            "资源管理器的缩略图/图标缓存数据库。多数文件被占用,清不掉的会自动跳过(可配合「重启资源管理器」后重试)。",
            "PaintBrush24",
            () => ScanDirs(
            [
                (LocalAppData + @"\Microsoft\Windows\Explorer", "thumbcache_*.db"),
                (LocalAppData + @"\Microsoft\Windows\Explorer", "iconcache_*.db"),
            ]),
            () => CleanDirs(
            [
                (LocalAppData + @"\Microsoft\Windows\Explorer", "thumbcache_*.db"),
                (LocalAppData + @"\Microsoft\Windows\Explorer", "iconcache_*.db"),
            ])),

        new("wer", "错误报告队列",
            "Windows 错误报告(WER)排队/已归档的报告,以世纪为单位没人看的东西。",
            "ErrorCircle24",
            () => ScanDirs(
            [
                (LocalAppData + @"\Microsoft\Windows\WER\ReportQueue", "*"),
                (LocalAppData + @"\Microsoft\Windows\WER\ReportArchive", "*"),
                (CommonAppData + @"\Microsoft\Windows\WER\ReportQueue", "*"),
                (CommonAppData + @"\Microsoft\Windows\WER\ReportArchive", "*"),
            ]),
            () => CleanDirs(
            [
                (LocalAppData + @"\Microsoft\Windows\WER\ReportQueue", "*"),
                (LocalAppData + @"\Microsoft\Windows\WER\ReportArchive", "*"),
                (CommonAppData + @"\Microsoft\Windows\WER\ReportQueue", "*"),
                (CommonAppData + @"\Microsoft\Windows\WER\ReportArchive", "*"),
            ])),

        new("crash-dumps", "应用崩溃转储",
            @"%LOCALAPPDATA%\CrashDumps 与 C:\Windows\Minidump 的崩溃内存转储,排查完问题就没用了。",
            "Bug24",
            () => ScanDirs(
            [
                (LocalAppData + @"\CrashDumps", "*"),
                (WinDir + @"\Minidump", "*"),
            ]),
            () => CleanDirs(
            [
                (LocalAppData + @"\CrashDumps", "*"),
                (WinDir + @"\Minidump", "*"),
            ])),

        new("recycle-bin", "回收站",
            "清空回收站(所有盘符,不可恢复!)。",
            "Recycle24",
            ScanRecycleBin,
            EmptyRecycleBin),

        new("edge-cache", "Edge 浏览器缓存",
            "各用户配置的网页缓存/代码缓存/GPU 缓存。清理前请关闭 Edge,占用文件会自动跳过。",
            "WindowApps24",
            () => ScanBrowserCaches(@"Microsoft\Edge\User Data"),
            () => CleanBrowserCaches(@"Microsoft\Edge\User Data")),

        new("chrome-cache", "Chrome 浏览器缓存",
            "各用户配置的网页缓存/代码缓存/GPU 缓存。清理前请关闭 Chrome,占用文件会自动跳过。",
            "Globe24",
            () => ScanBrowserCaches(@"Google\Chrome\User Data"),
            () => CleanBrowserCaches(@"Google\Chrome\User Data")),

        new("firefox-cache", "Firefox 浏览器缓存",
            "各 Profile 的 cache2 网页缓存。清理前请关闭 Firefox,占用文件会自动跳过。",
            "Sparkle24",
            () => ScanBrowserCaches(@"Mozilla\Firefox\Profiles"),
            () => CleanBrowserCaches(@"Mozilla\Firefox\Profiles")),

        new("recent-docs", "最近使用记录",
            "「最近使用的文件」列表与「运行」对话框历史(注册表记录,按条目计)。",
            "History24",
            ScanRecentDocs,
            CleanRecentDocs),
    ];

    // ---------------- 文件系统扫描/清理 ----------------

    private static (long bytes, int files) Measure(string dir, string filter, TimeSpan? minAge)
    {
        long bytes = 0;
        var files = 0;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return (0, 0);
        var cutoff = minAge == null ? DateTime.MinValue : DateTime.Now - minAge.Value;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint, // 跳过 junction,避免循环
        };
        foreach (var path in Directory.EnumerateFiles(dir, filter, options))
        {
            try
            {
                var info = new FileInfo(path);
                if (minAge != null && info.LastWriteTime > cutoff) continue;
                bytes += info.Length;
                files++;
            }
            catch { /* 占用/权限问题跳过 */ }
        }
        return (bytes, files);
    }

    private record DirRule(string Dir, string Filter);

    private static CleanupScanResult ScanDirs(List<(string dir, string filter)> rules, string? absoluteBase = null, TimeSpan? minAge = null)
    {
        long bytes = 0;
        var files = 0;
        foreach (var (dir, filter) in rules)
        {
            var path = Resolve(dir, absoluteBase);
            var (b, f) = Measure(path, filter, minAge);
            bytes += b;
            files += f;
        }
        return new CleanupScanResult(bytes, files);
    }

    private static CleanupResult CleanDirs(List<(string dir, string filter)> rules, string? absoluteBase = null, TimeSpan? minAge = null)
    {
        long freed = 0;
        var deleted = 0;
        var locked = 0;
        var cutoff = minAge == null ? DateTime.MinValue : DateTime.Now - minAge.Value;
        foreach (var (dir, filter) in rules)
        {
            var path = Resolve(dir, absoluteBase);
            if (!Directory.Exists(path)) continue;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            foreach (var filePath in Directory.EnumerateFiles(path, filter, options))
            {
                try
                {
                    var info = new FileInfo(filePath);
                    if (minAge != null && info.LastWriteTime > cutoff) continue;
                    var size = info.Length;
                    info.Delete();
                    freed += size;
                    deleted++;
                }
                catch (IOException) { locked++; }
                catch (UnauthorizedAccessException) { locked++; }
            }
            // 清掉删空后的残留空目录(不递归删除非空目录)
            TryRemoveEmptySubdirectories(path);
        }
        return new CleanupResult(freed, deleted, locked > 0 ? $"跳过 {locked} 个占用/受保护文件" : null);
    }

    private static void TryRemoveEmptySubdirectories(string dir)
    {
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(dir, "*", new EnumerationOptions { RecurseSubdirectories = true }))
                try { Directory.Delete(sub, recursive: false); } catch { /* 非空或占用则保留 */ }
        }
        catch { }
    }

    private static string Resolve(string dir, string? absoluteBase)
    {
        if (!string.IsNullOrEmpty(absoluteBase)) return absoluteBase == WinDir ? Path.Combine(WinDir, dir) : Path.Combine(absoluteBase, dir);
        return dir;
    }

    private static CleanupScanResult ScanBrowserCaches(string userDataRelative)
    {
        var baseDir = Path.Combine(LocalAppData, userDataRelative);
        long bytes = 0;
        var files = 0;
        foreach (var pair in EnumerateBrowserCacheDirs(baseDir))
        {
            var (b, f) = Measure(pair.Dir, pair.Filter, minAge: null);
            bytes += b;
            files += f;
        }
        return new CleanupScanResult(bytes, files);
    }

    private static CleanupResult CleanBrowserCaches(string userDataRelative)
    {
        var baseDir = Path.Combine(LocalAppData, userDataRelative);
        long freed = 0;
        var deleted = 0;
        var locked = 0;
        foreach (var pair in EnumerateBrowserCacheDirs(baseDir))
        {
            var result = CleanDirs([(pair.Dir, pair.Filter)]);
            freed += result.FreedBytes;
            deleted += result.Files;
            if (result.Note != null)
                locked += int.TryParse(result.Note.Replace("跳过 ", "").Replace(" 个占用/受保护文件", ""), out var n) ? n : 0;
        }
        return new CleanupResult(freed, deleted, locked > 0 ? $"跳过 {locked} 个占用/受保护文件" : null);
    }

    /// <summary>枚举浏览器缓存目录:Edge/Chrome 按 Default 与 Profile* 配置,Firefox 按 Profiles 下每个 profile。</summary>
    private static List<DirRule> EnumerateBrowserCacheDirs(string baseDir)
    {
        var rules = new List<DirRule>();
        if (!Directory.Exists(baseDir)) return rules;

        var isFirefox = baseDir.EndsWith("Profiles", StringComparison.OrdinalIgnoreCase);
        foreach (var profile in Directory.EnumerateDirectories(baseDir))
        {
            var name = Path.GetFileName(profile);
            var isProfile = isFirefox || name.Equals("Default", StringComparison.OrdinalIgnoreCase)
                            || name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
            if (!isProfile) continue;

            if (isFirefox)
            {
                rules.Add(new DirRule(Path.Combine(profile, "cache2"), "*"));
            }
            else
            {
                foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache" })
                    rules.Add(new DirRule(Path.Combine(profile, sub), "*"));
            }
        }
        return rules;
    }

    // ---------------- 回收站(shell32) ----------------

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinW(string? rootPath, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? rootPath, uint flags);

    private const uint SHERB_NOCONFIRMATION = 0x1;
    private const uint SHERB_NOPROGRESSUI = 0x2;
    private const uint SHERB_NOSOUND = 0x4;

    private static CleanupScanResult ScanRecycleBin()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        var hr = SHQueryRecycleBinW(null, ref info);
        if (hr != 0) return new CleanupScanResult(0, 0, "无法读取回收站状态");
        return info.i64NumItems == 0
            ? new CleanupScanResult(0, 0)
            : new CleanupScanResult(info.i64Size, (int)info.i64NumItems);
    }

    private static CleanupResult EmptyRecycleBin()
    {
        var scan = ScanRecycleBin();
        if (scan.Files == 0) return new CleanupResult(0, 0);
        var hr = SHEmptyRecycleBinW(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        return hr == 0
            ? new CleanupResult(scan.Bytes, scan.Files)
            : new CleanupResult(0, 0, "清空回收站失败(可能有系统级占用)");
    }

    // ---------------- 最近使用记录(注册表) ----------------

    private const string RecentDocsPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs";
    private const string TypedPathsPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths";

    private static CleanupScanResult ScanRecentDocs()
    {
        var count = CountRegistryValues(Registry.CurrentUser, RecentDocsPath);
        count += CountRegistryValues(Registry.CurrentUser, TypedPathsPath);
        return new CleanupScanResult(0, count);
    }

    private static CleanupResult CleanRecentDocs()
    {
        var before = CountRegistryValues(Registry.CurrentUser, RecentDocsPath)
                     + CountRegistryValues(Registry.CurrentUser, TypedPathsPath);
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(RecentDocsPath, throwOnMissingSubKey: false);
            Registry.CurrentUser.CreateSubKey(RecentDocsPath); // 资源管理器需要该键存在
        }
        catch (Exception ex)
        {
            Log.Warn("清空最近使用记录失败: " + ex.Message);
            return new CleanupResult(0, 0, "清理失败:" + ex.Message);
        }
        try
        {
            using var typedPaths = Registry.CurrentUser.OpenSubKey(TypedPathsPath, writable: true);
            if (typedPaths != null)
                foreach (var name in typedPaths.GetValueNames())
                    typedPaths.DeleteValue(name, throwOnMissingValue: false);
        }
        catch { /* TypedPaths 清不掉不致命 */ }
        return new CleanupResult(0, before);
    }

    private static int CountRegistryValues(RegistryKey root, string path)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            if (key == null) return 0;
            var count = key.GetValueNames().Length;
            foreach (var sub in key.GetSubKeyNames())
                count += CountRegistryValues(key, sub); // 递归统计各扩展名子键里的记录
            return count;
        }
        catch
        {
            return 0;
        }
    }
}
