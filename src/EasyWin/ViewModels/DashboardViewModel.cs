using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWin.Models;
using EasyWin.Services;

namespace EasyWin.ViewModels;

/// <summary>仪表盘:系统概览信息,1 秒刷新 CPU/内存/运行时长。</summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly SystemInfoService _system;
    private readonly NetworkService _network;
    private readonly DispatcherTimer _timer;
    private int _pingCounter;

    public DashboardViewModel(SystemInfoService system, NetworkService network)
    {
        _system = system;
        _network = network;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        if (_timer.IsEnabled) return;
        LoadStaticInfo();
        // 后台预热 CPU 计数器(首次采样初始化较慢,避免卡 UI)
        _ = System.Threading.Tasks.Task.Run(() => _system.GetCpuUsage());
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private async void LoadStaticInfo()
    {
        try
        {
            var os = await Task.Run(() => _system.GetOsInfo()).ConfigureAwait(true);
            OsName = string.IsNullOrWhiteSpace(os.Caption) ? "Windows" : os.Caption.Replace("Microsoft ", "");
            OsVersion = os.VersionText;
            OsArch = os.Architecture;
            CpuName = await Task.Run(() => _system.GetCpuName()).ConfigureAwait(true);

            var disks = await Task.Run(() => _system.GetDisks()).ConfigureAwait(true);
            _lastDiskSnapshot = DiskSnapshot(disks);
            Disks.Clear();
            foreach (var disk in disks) Disks.Add(disk);

            var adapters = await Task.Run(() => _network.GetAdapters()).ConfigureAwait(true);
            var active = adapters.FirstOrDefault(a => a.IsConnected);
            ActiveNetwork = active == null
                ? "未连接任何网络"
                : $"{active.ConnectionName} · {active.IpAddress}";

            var adaptersCount = adapters.Count;
            AdapterCountText = $"共 {adaptersCount} 块网卡,其中 {adapters.Count(a => a.IsEnabled)} 块已启用";

            ActivationText = await _system.GetActivationTextAsync().ConfigureAwait(true);

            UpdatePingNow();
        }
        catch (Exception ex)
        {
            Log.Error("加载系统信息失败", ex);
        }
    }

    private int _tickInProgress;

    /// <summary>采样计算放后台线程,结果回 UI 更新;带重入保护。</summary>
    private async void Tick()
    {
        if (System.Threading.Interlocked.Exchange(ref _tickInProgress, 1) == 1) return;
        try
        {
            var (memory, cpu, uptime) = await System.Threading.Tasks.Task.Run(() =>
                (_system.GetMemory(), _system.GetCpuUsage(), _system.GetUptime())).ConfigureAwait(true);

            MemTotalText = FormatBytes(memory.TotalBytes);
            MemUsedText = FormatBytes(memory.TotalBytes - memory.AvailableBytes);
            MemPercent = memory.TotalBytes == 0 ? 0 : (memory.TotalBytes - memory.AvailableBytes) * 100.0 / memory.TotalBytes;

            CpuPercent = double.IsNaN(cpu) ? 0 : Math.Clamp(cpu, 0, 100);

            UptimeText = uptime.Days > 0
                ? $"{uptime.Days} 天 {uptime.Hours} 小时 {uptime.Minutes} 分"
                : $"{uptime.Hours} 小时 {uptime.Minutes} 分";

            if (++_pingCounter % 10 == 0)
                UpdatePingNow();

            // 磁盘变化检测(U 盘插入/拔出自动刷新)
            var disks = await System.Threading.Tasks.Task.Run(() => _system.GetDisks()).ConfigureAwait(true);
            var snapshot = DiskSnapshot(disks);
            if (snapshot != _lastDiskSnapshot)
            {
                _lastDiskSnapshot = snapshot;
                Disks.Clear();
                foreach (var disk in disks) Disks.Add(disk);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("采样失败: " + ex.Message);
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _tickInProgress, 0);
        }
    }

    private string? _lastDiskSnapshot;

    private static string DiskSnapshot(IEnumerable<DiskInfo> disks) =>
        string.Join("|", disks.Select(d => $"{d.Drive}:{d.TotalBytes}:{d.FreeBytes}"));

    /// <summary>双击磁盘卡片,在资源管理器中打开对应盘符。</summary>
    [RelayCommand]
    private void OpenDisk(DiskInfo? disk)
    {
        if (disk == null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{disk.Drive}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error("打开磁盘失败", ex);
            Toast.Error("打开磁盘失败:" + ex.Message);
        }
    }

    /// <summary>安全弹出可移动磁盘(U 盘/移动硬盘)。</summary>
    [RelayCommand]
    private async Task EjectDiskAsync(DiskInfo? disk)
    {
        if (disk == null || !disk.IsRemovable) return;
        if (!await Ui.ConfirmAsync("安全弹出", $"确定弹出「{disk.Drive} {disk.Label}」吗?", "弹出前请确保磁盘上的文件都已关闭。")) return;

        var (ok, message) = await System.Threading.Tasks.Task.Run(
            () => UsbEjectService.Eject(disk.Drive)).ConfigureAwait(true);
        if (ok)
        {
            Toast.Success(message);
            await System.Threading.Tasks.Task.Delay(800).ConfigureAwait(true);
            var disks = await System.Threading.Tasks.Task.Run(() => _system.GetDisks()).ConfigureAwait(true);
            _lastDiskSnapshot = DiskSnapshot(disks);
            Disks.Clear();
            foreach (var d in disks) Disks.Add(d);
        }
        else
        {
            Toast.Error(message);
        }
    }

    private async void UpdatePingNow()
    {
        var reply = await _network.PingAsync("223.5.5.5").ConfigureAwait(true);
        InternetText = reply?.Status == System.Net.NetworkInformation.IPStatus.Success
            ? $"网络正常 · {reply.RoundtripTime} ms"
            : "网络不可达";
        InternetOk = reply?.Status == System.Net.NetworkInformation.IPStatus.Success;
    }

    private static string FormatBytes(ulong bytes) => bytes switch
    {
        >= 1UL << 30 => $"{bytes / (double)(1UL << 30):0.#} GB",
        >= 1UL << 20 => $"{bytes / (double)(1UL << 20):0.#} MB",
        _ => $"{bytes} B"
    };

    // ---------- 绑定属性 ----------

    [ObservableProperty] private string _osName = "检测中…";
    [ObservableProperty] private string _osVersion = "";
    [ObservableProperty] private string _osArch = "";
    [ObservableProperty] private string _cpuName = "";
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _memPercent;
    [ObservableProperty] private string _memTotalText = "—";
    [ObservableProperty] private string _memUsedText = "—";
    [ObservableProperty] private string _uptimeText = "—";
    [ObservableProperty] private string _activationText = "查询中…";
    [ObservableProperty] private string _activeNetwork = "检测中…";
    [ObservableProperty] private string _adapterCountText = "";
    [ObservableProperty] private string _internetText = "检测中…";
    [ObservableProperty] private bool _internetOk;
    [ObservableProperty] private string _computerName = Environment.MachineName;
    [ObservableProperty] private string _userName = Environment.UserName;

    public ObservableCollection<DiskInfo> Disks { get; } = [];
}
