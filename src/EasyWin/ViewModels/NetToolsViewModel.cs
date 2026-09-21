using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWin.Services;

namespace EasyWin.ViewModels;

/// <summary>网络工具页:系统代理、hosts 编辑、DNS 维护。</summary>
public partial class NetToolsViewModel : ObservableObject
{
    private readonly NetworkService _network;
    private bool _proxyInitializing = true;

    public NetToolsViewModel(NetworkService network)
    {
        _network = network;
        LoadProxy();
        LoadHosts();
    }

    // ---------------- 系统代理 ----------------

    [ObservableProperty]
    private bool _proxyEnabled;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveProxyCommand))]
    private string _proxyServer = "";

    [ObservableProperty]
    private string _proxyOverride = "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;<local>";

    [ObservableProperty]
    private string? _autoConfigUrl;

    partial void OnProxyEnabledChanged(bool value)
    {
        if (_proxyInitializing) return;
        // 切换开关即生效(使用当前填写的服务器地址)
        ApplyProxyAsync().ConfigureAwait(true);
    }

    private void LoadProxy()
    {
        var info = SysProxyService.Get();
        ProxyEnabled = info.Enabled;
        ProxyServer = info.Server;
        ProxyOverride = string.IsNullOrWhiteSpace(info.Override) ? ProxyOverride : info.Override;
        AutoConfigUrl = info.AutoConfigUrl;
        _proxyInitializing = false;
    }

    private bool CanSaveProxy() => !ProxyEnabled || IsValidProxyServer(ProxyServer);

    [RelayCommand(CanExecute = nameof(CanSaveProxy))]
    private async Task SaveProxyAsync() => await ApplyProxyAsync().ConfigureAwait(true);

    private async Task ApplyProxyAsync()
    {
        if (ProxyEnabled && !IsValidProxyServer(ProxyServer))
        {
            await Ui.AlertAsync("代理地址无效", "请按「地址:端口」的格式填写,例如 127.0.0.1:7890。");
            return;
        }

        await Task.Run(() => SysProxyService.Set(ProxyEnabled, ProxyServer.Trim(), ProxyOverride.Trim())).ConfigureAwait(true);
        Toast.Success(ProxyEnabled ? "系统代理已开启并立即生效" : "系统代理已关闭");
    }

    private static bool IsValidProxyServer(string? server)
    {
        if (string.IsNullOrWhiteSpace(server)) return false;
        var parts = server.Trim().Split(':');
        if (parts.Length != 2) return false;
        return int.TryParse(parts[1], out var port) && port is > 0 and < 65536 && parts[0].Length > 0;
    }

    partial void OnProxyServerChanged(string value) => SaveProxyCommand.NotifyCanExecuteChanged();

    // ---------------- hosts ----------------

    [ObservableProperty]
    private string _hostsText = "";

    [RelayCommand]
    private void LoadHosts()
    {
        try
        {
            HostsText = HostsService.Read();
        }
        catch (Exception ex)
        {
            Log.Error("读取 hosts 失败", ex);
            Toast.Error("读取 hosts 失败:" + ex.Message);
        }
    }

    [RelayCommand]
    private void SaveHosts()
    {
        try
        {
            var backup = HostsService.Save(HostsText);
            Toast.Success(backup == "" ? "hosts 已保存" : $"hosts 已保存,备份:{System.IO.Path.GetFileName(backup)}");
        }
        catch (Exception ex)
        {
            Log.Error("保存 hosts 失败", ex);
            Toast.Error("保存失败:" + ex.Message);
        }
    }

    // ---------------- DNS 维护 ----------------

    [RelayCommand]
    private async Task FlushDnsAsync()
    {
        await Task.Run(() => _ = CommandRunner.Run("ipconfig", "/flushdns")).ConfigureAwait(true);
        Toast.Success("DNS 解析缓存已刷新");
    }

    [RelayCommand]
    private async Task ResetWinsockAsync()
    {
        if (!await Ui.ConfirmAsync("重置 Winsock", "重置 Winsock 目录可以修复无法上网等网络问题。", "执行后需要重启电脑才能生效,确定继续吗?")) return;
        var result = await Task.Run(() => CommandRunner.Run("netsh", "winsock", "reset")).ConfigureAwait(true);
        Toast.Success(result.Ok ? "Winsock 已重置,请重启电脑生效" : "重置失败:" + result.AllText);
    }
}
