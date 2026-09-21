using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using EasyWin.Models;

namespace EasyWin.Services;

public record OsInfo(string Caption, string VersionText, string Architecture);
public record MemoryInfo(ulong TotalBytes, ulong AvailableBytes);
public record CpuSample(float UsagePercent);

/// <summary>系统信息:版本、激活、CPU/内存、磁盘、开机时长。</summary>
public class SystemInfoService
{
    private PerformanceCounter? _cpuCounter;

    public OsInfo GetOsInfo()
    {
        var caption = "";
        var version = Environment.OSVersion.Version.ToString();
        var arch = Environment.Is64BitOperatingSystem ? "64 位" : "32 位";
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Caption, OSArchitecture, Version FROM Win32_OperatingSystem");
            foreach (var o in searcher.Get())
            {
                caption = (string)o["Caption"];
                version = (string)o["Version"];
                arch = (string)o["OSArchitecture"];
                break;
            }
        }
        catch (Exception ex) { Log.Warn("读取系统版本失败: " + ex.Message); }

        // 补充显示版本(如 24H2)与修订号(Build 26100.1234)
        var displayVersion = "";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            displayVersion = key?.GetValue("DisplayVersion") as string ?? "";
            var build = key?.GetValue("CurrentBuildNumber") as string;
            var ubr = key?.GetValue("UBR") as int?;
            if (build != null)
                version = ubr.HasValue ? $"{build}.{ubr}" : build;
        }
        catch { /* 忽略,已有默认值 */ }

        return new OsInfo(caption, string.IsNullOrEmpty(displayVersion) ? version : $"{displayVersion} (Build {version})", arch);
    }

    public string GetCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string ?? "").Trim();
        }
        catch
        {
            return $"CPU × {Environment.ProcessorCount}";
        }
    }

    public MemoryInfo GetMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref status);
        return new MemoryInfo(status.ullTotalPhys, status.ullAvailPhys);
    }

    public TimeSpan GetUptime() => TimeSpan.FromMilliseconds(Environment.TickCount64);

    public List<DiskInfo> GetDisks()
    {
        var disks = new List<DiskInfo>();
        foreach (var drive in System.IO.DriveInfo.GetDrives())
        {
            // 固定硬盘 + U 盘等可移动设备
            if ((drive.DriveType != System.IO.DriveType.Fixed && drive.DriveType != System.IO.DriveType.Removable) || !drive.IsReady) continue;
            try
            {
                disks.Add(new DiskInfo(drive.Name, drive.VolumeLabel, drive.TotalSize, drive.AvailableFreeSpace, drive.DriveType == System.IO.DriveType.Removable));
            }
            catch { /* 个别盘符可能拒绝访问 */ }
        }
        return disks;
    }

    /// <summary>Windows 激活状态(WMI SoftwareLicensingProduct,查询较慢,调用方应放后台线程)。1=已激活。</summary>
    public async Task<string> GetActivationTextAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT LicenseStatus FROM SoftwareLicensingProduct " +
                    "WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL");
                foreach (var o in searcher.Get())
                {
                    return Convert.ToInt32(o["LicenseStatus"]) switch
                    {
                        1 => "已激活",
                        0 => "未激活",
                        2 => "已过期(宽限期外)",
                        3 => "宽限期内",
                        5 => "许可异常",
                        6 => "延长宽限期",
                        _ => $"状态码 {Convert.ToInt32(o["LicenseStatus"])}"
                    };
                }
                return "未找到有效许可证";
            }
            catch (Exception ex)
            {
                Log.Warn("查询激活状态失败: " + ex.Message);
                return "查询失败";
            }
        }).ConfigureAwait(false);
    }

    /// <summary>CPU 占用率(系统初始化时先预热一次,之后每次调用返回上一次采样区间内的值)。</summary>
    public float GetCpuUsage()
    {
        try
        {
            _cpuCounter ??= CreateCpuCounter();
            return _cpuCounter.NextValue();
        }
        catch (Exception ex)
        {
            Log.Warn("读取 CPU 占用失败: " + ex.Message);
            return 0f;
        }
    }

    private static PerformanceCounter CreateCpuCounter()
    {
        var counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        counter.NextValue(); // 第一次采样是无效值,丢掉
        return counter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
