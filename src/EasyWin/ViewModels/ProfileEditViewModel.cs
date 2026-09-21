using CommunityToolkit.Mvvm.ComponentModel;
using EasyWin.Models;

namespace EasyWin.ViewModels;

/// <summary>方案编辑窗口的绑定模型,包装 IpProfile 提供模式单选绑定。</summary>
public partial class ProfileEditViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _adapterName = "";
    [ObservableProperty] private string _ipAddress = "";
    [ObservableProperty] private string _subnetMask = "255.255.255.0";
    [ObservableProperty] private string _gateway = "";
    [ObservableProperty] private string _dns1 = "";
    [ObservableProperty] private string _dns2 = "";
    [ObservableProperty] private bool _isDhcp;
    [ObservableProperty] private bool _isStatic = true;

    public IReadOnlyList<string> AdapterNames { get; init; } = [];

    public static ProfileEditViewModel From(IpProfile profile, IReadOnlyList<string> adapterNames) => new()
    {
        Name = profile.Name,
        AdapterName = profile.AdapterName,
        IpAddress = profile.IpAddress,
        SubnetMask = string.IsNullOrEmpty(profile.SubnetMask) ? "255.255.255.0" : profile.SubnetMask,
        Gateway = profile.Gateway,
        Dns1 = profile.Dns1,
        Dns2 = profile.Dns2,
        IsDhcp = profile.Mode == IpConfigMode.Dhcp,
        IsStatic = profile.Mode == IpConfigMode.Static,
        AdapterNames = adapterNames,
    };

    public void CopyTo(IpProfile profile)
    {
        profile.Name = Name.Trim();
        profile.AdapterName = AdapterName;
        profile.Mode = IsDhcp ? IpConfigMode.Dhcp : IpConfigMode.Static;
        profile.IpAddress = IpAddress.Trim();
        profile.SubnetMask = SubnetMask.Trim();
        profile.Gateway = Gateway.Trim();
        profile.Dns1 = Dns1.Trim();
        profile.Dns2 = Dns2.Trim();
    }
}
