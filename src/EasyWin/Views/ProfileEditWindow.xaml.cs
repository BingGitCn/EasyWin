using System.Windows;
using EasyWin.Models;
using EasyWin.Services;
using EasyWin.ViewModels;
using Wpf.Ui.Controls;

namespace EasyWin.Views;

public partial class ProfileEditWindow : FluentWindow
{
    private readonly IpProfile _profile;

    /// <summary>点击保存后包含编辑结果;取消时为 null。</summary>
    public IpProfile? Result { get; private set; }

    public ProfileEditWindow(IpProfile profile, IReadOnlyList<string> adapterNames)
    {
        InitializeComponent();
        _profile = profile;
        DataContext = ProfileEditViewModel.From(profile, adapterNames);
        Title = string.IsNullOrEmpty(profile.Name) ? "新建方案" : "编辑方案";
        ContentRendered += (_, _) => NameBox.Focus();
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var vm = (ProfileEditViewModel)DataContext;

        if (string.IsNullOrWhiteSpace(vm.Name))
        {
            await Ui.AlertAsync("请填写方案名称", "方案名称不能为空。");
            return;
        }
        if (string.IsNullOrWhiteSpace(vm.AdapterName))
        {
            await Ui.AlertAsync("请选择网卡", "请选择该方案要应用到的网卡。");
            return;
        }
        if (!vm.IsDhcp)
        {
            if (!NetworkService.IsValidIPv4(vm.IpAddress))
            {
                await Ui.AlertAsync("参数无效", "IP 地址格式不正确。");
                return;
            }
            if (!NetworkService.IsValidMask(vm.SubnetMask))
            {
                await Ui.AlertAsync("参数无效", "子网掩码格式不正确。");
                return;
            }
            if (!string.IsNullOrWhiteSpace(vm.Gateway) && !NetworkService.IsValidIPv4(vm.Gateway))
            {
                await Ui.AlertAsync("参数无效", "网关格式不正确。");
                return;
            }
            if (!string.IsNullOrWhiteSpace(vm.Dns1) && !NetworkService.IsValidIPv4(vm.Dns1))
            {
                await Ui.AlertAsync("参数无效", "首选 DNS 格式不正确。");
                return;
            }
            if (!string.IsNullOrWhiteSpace(vm.Dns2) && !NetworkService.IsValidIPv4(vm.Dns2))
            {
                await Ui.AlertAsync("参数无效", "备用 DNS 格式不正确。");
                return;
            }
        }

        vm.CopyTo(_profile);
        Result = _profile;
        DialogResult = true;
    }
}
