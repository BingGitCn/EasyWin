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

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsWifiScanning) return;
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

    /// <summary>一键切换到指定 Wi-Fi。</summary>
    [RelayCommand]
    private async Task ConnectAsync(WlanNetwork? network)
    {
        if (network == null || IsWifiScanning) return;
        if (network.Connected) { Toast.Show($"已连接「{network.Ssid}」", ToastType.Info); return; }

        string? password = null;
        if (PromptWifiPassword != null)
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
            if (connected)
            {
                Toast.Success($"已连接「{network.Ssid}」");
                await RefreshAsync().ConfigureAwait(true);
            }
            else
            {
                Toast.Warning($"「{network.Ssid}」连接未完成(密码错误或信号不稳),请重试或检查密码。");
            }
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
