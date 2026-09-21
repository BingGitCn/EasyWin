using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWin.Models;
using EasyWin.Services;
using EasyWin.Views;

namespace EasyWin.ViewModels;

/// <summary>配置方案页:方案列表、新建/编辑/删除、一键应用。</summary>
public partial class ProfilesViewModel : ObservableObject
{
    private readonly NetworkService _network;
    private readonly ProfileStore _store;

    public ProfilesViewModel(NetworkService network, ProfileStore store)
    {
        _network = network;
        _store = store;
    }

    public ObservableCollection<IpProfile> Profiles { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var (profiles, adapters) = await Task.Run(() => (_store.Load(), _network.GetAdapters())).ConfigureAwait(true);
        var byName = new Dictionary<string, NetworkAdapterInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in adapters)
            byName.TryAdd(adapter.ConnectionName, adapter);

        Profiles.Clear();
        foreach (var profile in profiles.OrderBy(p => p.AdapterName).ThenBy(p => p.Name))
        {
            profile.InUse = byName.TryGetValue(profile.AdapterName, out var adapter) && MatchesAdapter(profile, adapter);
            Profiles.Add(profile);
        }
    }

    /// <summary>方案是否与网卡当前配置一致(用于"使用中"标记)。DHCP 方案只看模式;静态方案需 IP/掩码/网关/DNS 全部一致。</summary>
    private static bool MatchesAdapter(IpProfile p, NetworkAdapterInfo a)
    {
        if (p.Mode == IpConfigMode.Dhcp) return a.DhcpEnabled;
        if (a.DhcpEnabled) return false;

        if (!string.Equals(p.IpAddress.Trim(), a.IpAddress?.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (NetworkPrefix.PrefixFromMask(p.SubnetMask) != NetworkPrefix.PrefixFromMask(a.SubnetMask)) return false;

        var gatewayEmpty = string.IsNullOrWhiteSpace(p.Gateway);
        if (gatewayEmpty != string.IsNullOrWhiteSpace(a.Gateway)) return false;
        if (!gatewayEmpty && !string.Equals(p.Gateway.Trim(), a.Gateway!.Trim(), StringComparison.OrdinalIgnoreCase)) return false;

        return DnsMatches(p.Dns1, a.DnsServers) && DnsMatches(p.Dns2, a.DnsServers);
    }

    /// <summary>方案未指定 DNS(走 DHCP DNS)时不比对,避免 DHCP 环境下误判为不匹配。</summary>
    private static bool DnsMatches(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected)) return true;
        return (actual ?? "").Split(',').Any(d => d.Trim().Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        var adapters = await Task.Run(() => _network.GetAdapters()).ConfigureAwait(true);
        var profile = new IpProfile();
        if (EditProfile(profile, adapters.Select(a => a.ConnectionName).ToList(), out var saved))
        {
            var all = _store.Load();
            all.Add(saved);
            _store.Save(all);
            Toast.Success($"方案「{saved.Name}」已创建");
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>从网卡当前配置快速导入一个方案。</summary>
    [RelayCommand]
    private async Task ImportFromAdapterAsync()
    {
        var adapters = await Task.Run(() => _network.GetAdapters()).ConfigureAwait(true);
        if (adapters.Count == 0) { await Ui.AlertAsync("导入失败", "未找到任何网卡。"); return; }

        var profile = new IpProfile
        {
            AdapterName = adapters[0].ConnectionName,
            Mode = adapters[0].DhcpEnabled ? IpConfigMode.Dhcp : IpConfigMode.Static,
            IpAddress = adapters[0].IpAddress ?? "",
            SubnetMask = string.IsNullOrEmpty(adapters[0].SubnetMask) ? "255.255.255.0" : adapters[0].SubnetMask!,
            Gateway = adapters[0].Gateway ?? "",
            Dns1 = adapters[0].DnsServers?.Split(", ").FirstOrDefault() ?? "",
            Dns2 = adapters[0].DnsServers?.Split(", ").Skip(1).FirstOrDefault() ?? "",
            Name = $"{adapters[0].ConnectionName} 当前配置",
        };

        if (EditProfile(profile, adapters.Select(a => a.ConnectionName).ToList(), out var saved))
        {
            var all = _store.Load();
            all.Add(saved);
            _store.Save(all);
            Toast.Success($"方案「{saved.Name}」已保存");
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task EditAsync(IpProfile? profile)
    {
        if (profile == null) return;
        var adapters = await Task.Run(() => _network.GetAdapters()).ConfigureAwait(true);

        // 拷贝编辑,取消时不落盘
        var copy = new IpProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            AdapterName = profile.AdapterName,
            Mode = profile.Mode,
            IpAddress = profile.IpAddress,
            SubnetMask = profile.SubnetMask,
            Gateway = profile.Gateway,
            Dns1 = profile.Dns1,
            Dns2 = profile.Dns2,
            CreatedAt = profile.CreatedAt,
        };

        if (EditProfile(copy, adapters.Select(a => a.ConnectionName).ToList(), out var saved))
        {
            var all = _store.Load();
            var index = all.FindIndex(p => p.Id == saved.Id);
            if (index >= 0) all[index] = saved; else all.Add(saved);
            _store.Save(all);
            Toast.Success($"方案「{saved.Name}」已更新");
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(IpProfile? profile)
    {
        if (profile == null) return;
        if (!await Ui.ConfirmAsync("删除方案", $"确定删除方案「{profile.Name}」吗?", "此操作不可恢复。")) return;

        var all = _store.Load();
        all.RemoveAll(p => p.Id == profile.Id);
        _store.Save(all);
        Toast.Success($"方案「{profile.Name}」已删除");
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>一键切换:把方案应用到目标网卡。</summary>
    [RelayCommand]
    private async Task ApplyAsync(IpProfile? profile)
    {
        if (profile == null || IsBusy) return;

        var detail = profile.Mode == IpConfigMode.Dhcp
            ? "该网卡将改为 DHCP 自动获取。"
            : $"IP {profile.IpAddress}\n掩码 {profile.SubnetMask}\n网关 {(string.IsNullOrWhiteSpace(profile.Gateway) ? "(无)" : profile.Gateway)}";
        if (!await Ui.ConfirmAsync("一键切换", $"确定把「{profile.AdapterName}」切换到方案「{profile.Name}」吗?", detail + "\n\n切换过程中网络会短暂中断。")) return;

        IsBusy = true;
        try
        {
            var (ok, message) = await Task.Run(() => _network.ApplyProfileAsync(profile)).ConfigureAwait(true);
            if (ok)
            {
                Toast.Success(message);
                await Task.Delay(1500).ConfigureAwait(true); // 等 DHCP/路由生效,再刷新"使用中"标记
            }
            else Toast.Error(message);
        }
        catch (Exception ex)
        {
            Log.Error("应用方案失败", ex);
            Toast.Error("应用方案失败:" + ex.Message);
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync().ConfigureAwait(true); // 失败也可能改了一半配置,统一按实际状态刷新
        }
    }

    /// <summary>打开编辑窗口并等待结果;返回 true 表示用户确认了保存。</summary>
    private static bool EditProfile(IpProfile profile, List<string> adapterNames, out IpProfile saved)
    {
        if (adapterNames.Count > 0 && string.IsNullOrEmpty(profile.AdapterName))
            profile.AdapterName = adapterNames[0];

        var window = new ProfileEditWindow(profile, adapterNames) { Owner = Application.Current.MainWindow };
        var ok = window.ShowDialog() == true;
        saved = window.Result ?? profile;
        return ok;
    }
}
