using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using EasyWin.Models;

namespace EasyWin.Services;

/// <summary>
/// 第三方远程工具(向日葵/ToDesk/UU远程等)的本机检测与启动。
/// 检测顺序:注册表卸载信息 → 常见安装路径 → 运行中的进程,找到主程序即视为已安装。
/// </summary>
public static class RemoteToolService
{
    private sealed record ToolDef(
        string Name, string Description, string Url,
        string[] RegistryKeywords, string[] ProcessNames,
        string[] ExeNames, string[] KnownPaths);

    private static readonly ToolDef[] Definitions =
    [
        new("向日葵", "Oray 出品,国内老牌远控", "https://sunlogin.oray.com",
            ["向日葵", "Sunlogin"], ["SunloginClient"],
            ["SunloginClient.exe"],
            [@"Oray\SunLogin\SunloginClient\SunloginClient.exe"]),
        new("ToDesk", "国产远控,连接流畅", "https://www.todesk.com",
            ["ToDesk"], ["ToDesk"],
            ["ToDesk.exe"],
            [@"ToDesk\ToDesk.exe"]),
        new("网易UU远程", "网易出品,高清低延迟", "https://uuyc.163.com",
            ["UU远程", "UURemote"], ["UURemote"],
            ["UURemote.exe"],
            [@"NetEase\UURemote\UURemote.exe", @"NetEase\UU远程\UURemote.exe"]),
        new("AnyDesk", "轻量级,跨平台", "https://anydesk.com/zhs",
            ["AnyDesk"], ["AnyDesk"],
            ["AnyDesk.exe"],
            [@"AnyDesk\AnyDesk.exe"]),
        new("RustDesk", "开源远控,可自建中继", "https://rustdesk.com/zh-cn",
            ["RustDesk"], ["rustdesk"],
            ["rustdesk.exe"],
            [@"RustDesk\rustdesk.exe"]),
        new("TeamViewer", "国际主流,商用需授权", "https://www.teamviewer.com/zh-cn",
            ["TeamViewer"], ["TeamViewer"],
            ["TeamViewer.exe"],
            [@"TeamViewer\TeamViewer.exe"]),
    ];

    public static List<RemoteToolInfo> DetectAll()
    {
        var uninstall = LoadUninstallEntries();
        var runningPaths = FindRunningToolPaths();

        var result = new List<RemoteToolInfo>();
        foreach (var def in Definitions)
        {
            string? exe = null;

            // 1) 注册表卸载信息:DisplayIcon / InstallLocation 里找主程序
            var hit = uninstall.FirstOrDefault(e => def.RegistryKeywords.Any(k =>
                e.DisplayName.Contains(k, StringComparison.OrdinalIgnoreCase)));
            if (hit != null)
                exe = ResolveExeFromEntry(hit, def);

            // 2) 常见安装路径(ProgramFiles / ProgramFiles(x86))
            exe ??= FindInProgramFiles(def);

            // 3) 正在运行的进程路径
            exe ??= def.ProcessNames.Select(n => runningPaths.GetValueOrDefault(n)).FirstOrDefault(p => p != null);

            var info = new RemoteToolInfo
            {
                Name = def.Name,
                Description = def.Description,
                Url = def.Url,
                ExePath = exe,
                Installed = exe != null,
            };
            if (exe != null) info.Icon = TryExtractIcon(exe);
            result.Add(info);
        }
        return result;
    }

    public static (bool ok, string message) Launch(RemoteToolInfo tool)
    {
        if (!tool.Installed || string.IsNullOrEmpty(tool.ExePath) || !File.Exists(tool.ExePath))
            return (false, $"未检测到「{tool.Name}」,请先安装");
        try
        {
            Process.Start(new ProcessStartInfo(tool.ExePath) { UseShellExecute = true });
            return (true, $"正在打开 {tool.Name}");
        }
        catch (Exception ex)
        {
            Log.Error($"启动 {tool.Name} 失败", ex);
            return (false, "打开失败:" + ex.Message);
        }
    }

    public static void OpenWebsite(RemoteToolInfo tool)
    {
        try
        {
            Process.Start(new ProcessStartInfo(tool.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"打开 {tool.Name} 官网失败", ex);
            Toast.Error("打开官网失败:" + ex.Message);
        }
    }

    // ---------------- 检测细节 ----------------

    private sealed record UninstallEntry(string DisplayName, string InstallLocation, string DisplayIcon);

    private static List<UninstallEntry> LoadUninstallEntries()
    {
        var list = new List<UninstallEntry>();
        var roots = new[]
        {
            (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Microsoft.Win32.Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        };
        foreach (var (root, path) in roots)
        {
            try
            {
                using var key = root.OpenSubKey(path);
                if (key == null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    try
                    {
                        using var item = key.OpenSubKey(sub);
                        var name = item?.GetValue("DisplayName") as string;
                        if (string.IsNullOrEmpty(name)) continue;
                        list.Add(new UninstallEntry(
                            name,
                            item?.GetValue("InstallLocation") as string ?? "",
                            item?.GetValue("DisplayIcon") as string ?? ""));
                    }
                    catch { /* 单条读失败忽略 */ }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("读取卸载注册表失败: " + ex.Message);
            }
        }
        return list;
    }

    private static string? FindInProgramFiles(ToolDef def)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };
        foreach (var root in roots)
        {
            foreach (var known in def.KnownPaths)
            {
                var candidate = Path.Combine(root, known);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static string? ResolveExeFromEntry(UninstallEntry entry, ToolDef def)
    {
        // DisplayIcon 形如 "C:\x\y.exe,0",去掉参数与引号;卸载器(unins*)不可作为主程序
        var icon = entry.DisplayIcon.Split(',')[0].Trim('"').Trim();
        if (!string.IsNullOrEmpty(icon) && File.Exists(icon)
            && !Path.GetFileName(icon).StartsWith("unins", StringComparison.OrdinalIgnoreCase)
            && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return icon;

        // InstallLocation + 常见主程序名
        if (!string.IsNullOrEmpty(entry.InstallLocation))
        {
            foreach (var name in def.ExeNames)
            {
                var candidate = Path.Combine(entry.InstallLocation, name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static Dictionary<string, string> FindRunningToolPaths()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = Definitions.SelectMany(d => d.ProcessNames).Distinct().ToList();
        foreach (var name in names)
        {
            try
            {
                var procs = Process.GetProcessesByName(name);
                foreach (var p in procs)
                {
                    using (p)
                    {
                        try
                        {
                            var path = p.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(path) && !map.ContainsKey(name)) map[name] = path;
                        }
                        catch { // 系统进程/权限不足时读不到模块
                        }
                    }
                }
            }
            catch { /* 进程已退出等 */ }
        }
        return map;
    }

    private static ImageSource? TryExtractIcon(string exePath)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon == null) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }
}
