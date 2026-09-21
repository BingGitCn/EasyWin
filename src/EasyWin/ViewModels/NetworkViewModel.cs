using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWin.Models;
using EasyWin.Services;
using EasyWin.Views;

namespace EasyWin.ViewModels;

/// <summary>网络页:网卡列表 + 当前网卡的 IP 配置(DHCP/静态)与启停。</summary>
public partial class NetworkViewModel : ObservableObject
{
    private readonly NetworkService _network;
    private static readonly string[] CommonDns = ["223.5.5.5", "114.114.114.114", "8.8.8.8", "119.29.29.29"];

    public NetworkViewModel(NetworkService network)
    {
        _network = network;
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
    }

    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableAdapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableAdapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAsProfileCommand))]
    private NetworkAdapterInfo? _selectedAdapter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isStaticMode = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _ipAddress = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _subnetMask = "255.255.255.0";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _gateway = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _dns1 = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _dns2 = "";

    [ObservableProperty]
    private bool _isBusy;

    public IReadOnlyList<string> CommonDnsServers => CommonDns;

    public event Action? AdapterStateChanged;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var adapters = await Task.Run(() => _network.GetAdapters()).ConfigureAwait(true);
        var selected = SelectedAdapter?.ConnectionName;

        Adapters.Clear();
        foreach (var adapter in adapters) Adapters.Add(adapter);

        SelectedAdapter = Adapters.FirstOrDefault(a => a.ConnectionName == selected)
                          ?? Adapters.FirstOrDefault();
    }

    partial void OnSelectedAdapterChanged(NetworkAdapterInfo? value)
    {
        if (value == null) return;
        IsStaticMode = !value.DhcpEnabled;
        IpAddress = value.IpAddress ?? "";
        SubnetMask = string.IsNullOrEmpty(value.SubnetMask) ? "255.255.255.0" : value.SubnetMask;
        Gateway = value.Gateway ?? "";
        var dns = value.DnsServers?.Split(", ").FirstOrDefault() ?? "";
        Dns1 = dns;
        Dns2 = value.DnsServers?.Split(", ").Skip(1).FirstOrDefault() ?? "";
        ApplyCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var adapter = SelectedAdapter;
        if (adapter == null) return;

        if (!adapter.IsEnabled &&
            !await Ui.ConfirmAsync("网卡已禁用", $"「{adapter.ConnectionName}」当前处于禁用状态,配置可能无法完整生效。", "建议先启用网卡再修改 IP 配置。仍要继续吗?"))
            return;

        if (!IsStaticMode)
        {
            if (!await Ui.ConfirmAsync("切换为 DHCP", $"确定让「{adapter.ConnectionName}」自动获取 IP 和 DNS 吗?", "切换过程中网络会短暂中断。")) return;
        }
        else
        {
            if (!await ValidateStaticInput()) return;
            var detail = $"IP {IpAddress.Trim()}\n掩码 {SubnetMask.Trim()}\n网关 {(string.IsNullOrWhiteSpace(Gateway) ? "(无)" : Gateway.Trim())}\nDNS {DnsSummary()}";
            if (!await Ui.ConfirmAsync($"应用静态 IP — {adapter.ConnectionName}", "即将应用以下配置:", detail)) return;
        }

        await RunBusyAsync(async () =>
        {
            CmdResult addressResult = IsStaticMode
                ? await _network.SetStaticIpAsync(adapter.ConnectionName, IpAddress.Trim(), SubnetMask.Trim(), NullIfEmpty(Gateway)).ConfigureAwait(true)
                : await _network.SetDhcpIpAsync(adapter.ConnectionName).ConfigureAwait(true);

            if (!addressResult.Ok)
            {
                Toast.Error($"IP 设置失败:{addressResult.AllText}");
                return;
            }

            CmdResult dnsResult = IsStaticMode
                ? await _network.SetStaticDnsAsync(adapter.ConnectionName, NullIfEmpty(Dns1), NullIfEmpty(Dns2)).ConfigureAwait(true)
                : await _network.SetDhcpDnsAsync(adapter.ConnectionName).ConfigureAwait(true);

            if (!dnsResult.Ok) Toast.Error($"IP 已生效,但 DNS 设置失败:{dnsResult.AllText}");
            else Toast.Success(IsStaticMode
                ? $"{adapter.ConnectionName} 静态配置已应用"
                : $"{adapter.ConnectionName} 已切换为 DHCP");

            await RefreshAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private bool CanApply() => SelectedAdapter != null && !IsBusy
        && (!IsStaticMode || (NetworkService.IsValidIPv4(IpAddress) && NetworkService.IsValidMask(SubnetMask)));

    private async Task<bool> ValidateStaticInput()
    {
        if (!NetworkService.IsValidIPv4(IpAddress)) { await Ui.AlertAsync("参数无效", "IP 地址格式不正确。"); return false; }
        if (!NetworkService.IsValidMask(SubnetMask)) { await Ui.AlertAsync("参数无效", "子网掩码格式不正确。"); return false; }
        if (!string.IsNullOrWhiteSpace(Gateway) && !NetworkService.IsValidIPv4(Gateway)) { await Ui.AlertAsync("参数无效", "网关格式不正确。"); return false; }
        if (!string.IsNullOrWhiteSpace(Dns1) && !NetworkService.IsValidIPv4(Dns1)) { await Ui.AlertAsync("参数无效", "首选 DNS 格式不正确。"); return false; }
        if (!string.IsNullOrWhiteSpace(Dns2) && !NetworkService.IsValidIPv4(Dns2)) { await Ui.AlertAsync("参数无效", "备用 DNS 格式不正确。"); return false; }
        return true;
    }

    private string DnsSummary()
    {
        if (string.IsNullOrWhiteSpace(Dns1)) return "自动";
        return string.IsNullOrWhiteSpace(Dns2) ? Dns1 : $"{Dns1} / {Dns2}";
    }

    [RelayCommand(CanExecute = nameof(CanToggleState))]
    private async Task EnableAdapterAsync()
    {
        if (SelectedAdapter == null) return;
        await RunBusyAsync(async () =>
        {
            var result = await _network.SetAdapterStateAsync(SelectedAdapter.ConnectionName, enable: true).ConfigureAwait(true);
            if (result.Ok) Toast.Success($"{SelectedAdapter.ConnectionName} 已启用");
            else Toast.Error($"启用失败:{result.AllText}");
            await Task.Delay(1200).ConfigureAwait(true); // 等系统刷新状态
            await RefreshAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanToggleState))]
    private async Task DisableAdapterAsync()
    {
        var adapter = SelectedAdapter;
        if (adapter == null) return;
        if (!await Ui.ConfirmAsync("禁用网卡", $"确定禁用「{adapter.ConnectionName}」吗?", "禁用后该网卡将无法联网,可随时在此重新启用。")) return;

        await RunBusyAsync(async () =>
        {
            var result = await _network.SetAdapterStateAsync(adapter.ConnectionName, enable: false).ConfigureAwait(true);
            if (result.Ok) Toast.Success($"{adapter.ConnectionName} 已禁用");
            else Toast.Error($"禁用失败:{result.AllText}");
            await Task.Delay(800).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private bool CanToggleState() => SelectedAdapter != null && !IsBusy;

    /// <summary>打开"网络连接"面板(右键网卡即可看到属性;系统限制无法从命令行直达单网卡属性)。</summary>
    [RelayCommand]
    private void OpenAdapterProperties()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ncpa.cpl",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error("打开网络连接失败", ex);
            Toast.Error("打开失败:" + ex.Message);
        }
    }

    /// <summary>把当前表单保存为一个配置方案。</summary>
    [RelayCommand(CanExecute = nameof(CanSaveAsProfile))]
    private void SaveAsProfile()
    {
        var adapter = SelectedAdapter;
        if (adapter == null) return;

        var profile = new IpProfile
        {
            AdapterName = adapter.ConnectionName,
            Mode = IsStaticMode ? IpConfigMode.Static : IpConfigMode.Dhcp,
            IpAddress = IpAddress.Trim(),
            SubnetMask = SubnetMask.Trim(),
            Gateway = Gateway.Trim(),
            Dns1 = Dns1.Trim(),
            Dns2 = Dns2.Trim(),
            Name = $"{adapter.ConnectionName}-{NetworkService.PrefixFromMask(SubnetMask)}",
        };

        var window = new ProfileEditWindow(profile, Adapters.Select(a => a.ConnectionName).ToList()) { Owner = Application.Current.MainWindow };
        if (window.ShowDialog() == true && window.Result != null)
        {
            var store = App.GetService<ProfileStore>();
            var all = store.Load();
            all.Add(window.Result);
            store.Save(all);
            Toast.Success($"方案「{window.Result.Name}」已保存");
        }
    }

    private bool CanSaveAsProfile() => SelectedAdapter != null;

    private async Task RunBusyAsync(Func<Task> action)
    {
        IsBusy = true;
        try { await action().ConfigureAwait(true); }
        catch (Exception ex)
        {
            Log.Error("网络操作异常", ex);
            Toast.Error("操作失败:" + ex.Message);
        }
        finally
        {
            IsBusy = false;
            AdapterStateChanged?.Invoke();
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>系统网络地址变化(插网线/连 Wi-Fi 等)后自动刷新列表,做 500ms 防抖。</summary>
    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        if (_debounceTimer > 0) return;
        _debounceTimer = 1;
        Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(500).ConfigureAwait(true);
            _debounceTimer = 0;
            if (Application.Current == null) return;
            await RefreshAsync().ConfigureAwait(true);
        });
    }

    private int _debounceTimer;
}
