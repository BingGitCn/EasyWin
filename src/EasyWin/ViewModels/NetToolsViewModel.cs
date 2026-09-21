using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWin.Models;
using EasyWin.Services;

namespace EasyWin.ViewModels;

/// <summary>网络工具页:系统代理、hosts 编辑、DNS 维护。</summary>
public partial class NetToolsViewModel : ObservableObject
{
    private readonly NetworkService _network;
    private readonly ProxyStore _proxyStore;
    private bool _proxyInitializing = true;

    public NetToolsViewModel(NetworkService network, ProxyStore proxyStore)
    {
        _network = network;
        _proxyStore = proxyStore;
        LoadProxy();
        LoadHosts();
        LoadProxyProfiles();
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

        try
        {
            await Task.Run(() => SysProxyService.Set(ProxyEnabled, ProxyServer.Trim(), ProxyOverride.Trim())).ConfigureAwait(true);
            Toast.Success(ProxyEnabled ? "系统代理已开启并立即生效" : "系统代理已关闭");
        }
        catch (Exception ex)
        {
            Log.Error("设置系统代理失败", ex);
            Toast.Error("设置系统代理失败:" + ex.Message);
        }
    }

    private static bool IsValidProxyServer(string? server)
    {
        if (string.IsNullOrWhiteSpace(server)) return false;
        var parts = server.Trim().Split(':');
        if (parts.Length != 2) return false;
        return int.TryParse(parts[1], out var port) && port is > 0 and < 65536 && parts[0].Length > 0;
    }

    partial void OnProxyServerChanged(string value) => SaveProxyCommand.NotifyCanExecuteChanged();

    // ---------------- 代理方案 ----------------

    public System.Collections.ObjectModel.ObservableCollection<ProxyProfile> ProxyProfiles { get; } = [];

    private void LoadProxyProfiles()
    {
        ProxyProfiles.Clear();
        foreach (var profile in _proxyStore.Load()) ProxyProfiles.Add(profile);
    }

    /// <summary>把当前代理设置保存为命名方案(同名覆盖)。</summary>
    [RelayCommand]
    private void SaveProxyAsProfile()
    {
        if (!IsValidProxyServer(ProxyServer))
        {
            Toast.Warning("当前代理地址无效,请先修正再保存");
            return;
        }
        var name = Views.InputWindow.Show(
            Application.Current.MainWindow!, "保存代理方案", "给这组代理起个名字:", $"代理 {ProxyServer.Trim()}");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();

        var all = _proxyStore.Load();
        var existing = all.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Server = ProxyServer.Trim();
            existing.Override = ProxyOverride.Trim();
            Toast.Success($"代理方案「{name}」已更新");
        }
        else
        {
            all.Add(new ProxyProfile { Name = name, Server = ProxyServer.Trim(), Override = ProxyOverride.Trim() });
            Toast.Success($"代理方案「{name}」已保存");
        }
        _proxyStore.Save(all);
        LoadProxyProfiles();
    }

    /// <summary>一键应用某个代理方案(打开系统代理并生效)。</summary>
    [RelayCommand]
    private void ApplyProxyProfile(ProxyProfile? profile)
    {
        if (profile == null) return;
        ProxyServer = profile.Server;
        if (!string.IsNullOrWhiteSpace(profile.Override)) ProxyOverride = profile.Override;
        if (ProxyEnabled)
            _ = ApplyProxyAsync(); // 开关已开时不会触发属性变更,手动应用
        else
            ProxyEnabled = true;   // 触发 OnProxyEnabledChanged 走统一应用路径
        Toast.Success($"已切换代理方案「{profile.Name}」");
    }

    [RelayCommand]
    private void DeleteProxyProfile(ProxyProfile? profile)
    {
        if (profile == null) return;
        var all = _proxyStore.Load();
        all.RemoveAll(p => p.Id == profile.Id);
        _proxyStore.Save(all);
        LoadProxyProfiles();
        Toast.Success($"代理方案「{profile.Name}」已删除");
    }

    // ---------------- 端口占用 ----------------

    public System.Collections.ObjectModel.ObservableCollection<PortUsage> PortUsages { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckPortCommand))]
    private string _portText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckPortCommand))]
    private bool _isCheckingPort;

    private bool CanCheckPort() => int.TryParse(PortText.Trim(), out var p) && p is > 0 and < 65536 && !IsCheckingPort;

    [RelayCommand(CanExecute = nameof(CanCheckPort))]
    private async Task CheckPortAsync()
    {
        IsCheckingPort = true;
        try
        {
            var port = int.Parse(PortText.Trim());
            var usages = await Task.Run(() => PortLookup.FindUsages(port)).ConfigureAwait(true);
            PortUsages.Clear();
            foreach (var u in usages) PortUsages.Add(u);
            if (usages.Count == 0) Toast.Show($"端口 {port} 当前没有被占用", ToastType.Info);
        }
        catch (Exception ex)
        {
            Log.Error("查询端口占用失败", ex);
            Toast.Error("查询失败:" + ex.Message);
        }
        finally
        {
            IsCheckingPort = false;
        }
    }

    [RelayCommand]
    private async Task KillPortProcessAsync(PortUsage? usage)
    {
        if (usage == null) return;
        if (usage.ProcessId == Environment.ProcessId)
        {
            Toast.Warning("不能结束 EasyWin 自己的进程。");
            return;
        }
        if (!await Ui.ConfirmAsync("结束进程",
                $"确定结束进程「{usage.ProcessName}」(PID {usage.ProcessId})吗?",
                "它正占用端口 " + usage.Local.Split(':').Last() + "。结束进程会强制关闭该程序,未保存的数据会丢失。"))
            return;

        try
        {
            await Task.Run(() =>
            {
                using var p = System.Diagnostics.Process.GetProcessById(usage.ProcessId);
                p.Kill(entireProcessTree: true);
            }).ConfigureAwait(true);
            Toast.Success($"已结束 {usage.ProcessName}(PID {usage.ProcessId})");
            await CheckPortAsync().ConfigureAwait(true); // 复查占用是否解除
        }
        catch (Exception ex)
        {
            Log.Error($"结束进程 {usage.ProcessId} 失败", ex);
            Toast.Error("结束进程失败:" + ex.Message);
        }
    }

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
        var result = await Task.Run(() => CommandRunner.Run("ipconfig", "/flushdns")).ConfigureAwait(true);
        if (result.Ok) Toast.Success("DNS 解析缓存已刷新");
        else Toast.Error("刷新 DNS 缓存失败:" + result.AllText);
    }

    [RelayCommand]
    private async Task ResetWinsockAsync()
    {
        if (!await Ui.ConfirmAsync("重置 Winsock", "重置 Winsock 目录可以修复无法上网等网络问题。", "执行后需要重启电脑才能生效,确定继续吗?")) return;
        var result = await Task.Run(() => CommandRunner.Run("netsh", "winsock", "reset")).ConfigureAwait(true);
        Toast.Success(result.Ok ? "Winsock 已重置,请重启电脑生效" : "重置失败:" + result.AllText);
    }
}
