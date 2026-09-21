using System.Threading;
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWin.Models;
using EasyWin.Services;

namespace EasyWin.ViewModels;

/// <summary>Wi-Fi 页:扫描附近网络、一键切换连接。</summary>
public partial class WifiViewModel : ObservableObject
{
    private readonly NetworkService _network;

    /// <summary>Wi-Fi 密码输入窗口的回调,由视图层注入(需要窗口 Owner)。</summary>
    public Func<string, string, Task<(bool confirmed, string? password)>>? PromptWifiPassword { get; set; }

    public WifiViewModel(NetworkService network)
    {
        _network = network;
    }

    public ObservableCollection<WlanNetwork> Networks { get; } = [];

    [ObservableProperty] private bool _isWlanAvailable = true;
    [ObservableProperty] private string _currentWifiText = "";
    [ObservableProperty] private bool _isWifiScanning;

    /// <summary>最近一次成功扫描时间,托盘菜单等据此决定是否需要补扫。</summary>
    public DateTime LastScanAt { get; private set; } = DateTime.MinValue;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsWifiScanning) return;
        await ScanAsyncCore().ConfigureAwait(true);
    }

    /// <summary>实际扫描逻辑,不带"扫描中"守卫;连接完成后复用,否则会被自己的 IsWifiScanning 拦住。</summary>
    private async Task ScanAsyncCore()
    {
        IsWifiScanning = true;
        try
        {
            var (available, networks, current) = await Task.Run(() =>
            {
                if (!WlanService.IsWlanAvailable())
                    return (false, new List<WlanNetwork>(), (string?)null);

                // 先触发系统主动扫描,否则 netsh 只返回缓存的旧结果
                NativeWifiService.TriggerScan();
                Thread.Sleep(2800);

                var cur = WlanService.GetCurrentSsid();
                var networks = WlanService.ListNetworks(cur);
                if (networks.Count <= 1)
                {
                    Thread.Sleep(2200); // 结果太少再等一次扫描完成
                    networks = WlanService.ListNetworks(cur);
                }
                return (true, networks, cur);
            }).ConfigureAwait(true);

            IsWlanAvailable = available;
            if (available)
            {
                Networks.Clear();
                foreach (var n in networks) Networks.Add(n);
                CurrentWifiText = current == null ? "未连接 Wi-Fi" : $"当前 Wi-Fi:{current}";
                LastScanAt = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            Log.Error("扫描 Wi-Fi 失败", ex);
            Toast.Error("扫描 Wi-Fi 失败:" + ex.Message);
        }
        finally
        {
            IsWifiScanning = false;
        }
    }

    // ---------------- 已保存的 Wi-Fi 密码 ----------------

    public ObservableCollection<string> SavedProfiles { get; } = [];

    [ObservableProperty] private string? _selectedSavedProfile;
    [ObservableProperty] private string _savedPasswordText = "";

    /// <summary>最近一次读取到的明文密码,复制用。</summary>
    private string? _savedPasswordRaw;

    /// <summary>页面加载时调用:列出本机已保存的 Wi-Fi 配置文件。</summary>
    public async Task LoadSavedProfilesAsync()
    {
        try
        {
            var profiles = await Task.Run(WlanService.ListSavedProfiles).ConfigureAwait(true);
            SavedProfiles.Clear();
            foreach (var p in profiles) SavedProfiles.Add(p);
            SelectedSavedProfile = SavedProfiles.FirstOrDefault();
            SavedPasswordText = "";
            _savedPasswordRaw = null;
        }
        catch (Exception ex)
        {
            Log.Error("读取已保存 Wi-Fi 列表失败", ex);
        }
    }

    [RelayCommand]
    private async Task ShowSavedPasswordAsync()
    {
        if (SelectedSavedProfile == null)
        {
            await Ui.AlertAsync("未选择网络", "请先在下拉框选择一个已保存的 Wi-Fi。");
            return;
        }
        var name = SelectedSavedProfile;
        var password = await Task.Run(() => WlanService.GetSavedPassword(name)).ConfigureAwait(true);
        _savedPasswordRaw = password;
        SavedPasswordText = password switch
        {
            null => $"读取「{name}」的密码失败(配置文件可能刚被删除)。",
            "" => $"「{name}」是开放网络,没有密码。",
            _ => password,
        };
        if (password is { Length: > 0 })
            Toast.Show("密码已显示,注意不要泄露给不相关的人", ToastType.Info);
    }

    [RelayCommand]
    private void CopySavedPassword()
    {
        if (string.IsNullOrEmpty(_savedPasswordRaw))
        {
            Toast.Warning("还没有可复制的密码,请先「查看密码」。");
            return;
        }
        try
        {
            Clipboard.SetText(_savedPasswordRaw);
            Toast.Success("密码已复制到剪贴板");
        }
        catch (Exception ex)
        {
            Toast.Error("复制失败:" + ex.Message);
        }
    }

    /// <summary>一键切换到指定 Wi-Fi。</summary>
    [RelayCommand]
    private async Task ConnectAsync(WlanNetwork? network)
    {
        if (network == null || IsWifiScanning) return;
        if (network.Connected) { Toast.Show($"已连接「{network.Ssid}」", ToastType.Info); return; }

        string? password = null;
        // 开放网络无需密码,直接建配置文件连接,不再弹密码框
        if (!WlanService.IsOpenNetwork(network.Auth) && PromptWifiPassword != null)
        {
            var (confirmed, pwd) = await PromptWifiPassword(network.Ssid, network.Auth).ConfigureAwait(true);
            if (!confirmed) return;
            password = pwd;
        }

        IsWifiScanning = true;
        try
        {
            var (ok, message) = await Task.Run(() => WlanService.ConnectAsync(network.Ssid, password, network.Auth)).ConfigureAwait(true);
            if (!ok) { Toast.Error(message); return; }

            var connected = await Task.Run(() => WlanService.WaitConnectedAsync(network.Ssid)).ConfigureAwait(true);
            if (connected) Toast.Success($"已连接「{network.Ssid}」");
            else Toast.Warning($"「{network.Ssid}」连接未完成(密码错误或信号不稳),请重试或检查密码。");

            // 无论等待是否超时都重扫一次,让"已连接"徽章与实际连接状态一致
            await ScanAsyncCore().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error("切换 Wi-Fi 失败", ex);
            Toast.Error("切换失败:" + ex.Message);
        }
        finally
        {
            IsWifiScanning = false;
        }
    }
}
