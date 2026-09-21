using System.Windows;
using System.Windows.Controls;
using EasyWin.Models;
using EasyWin.Services;
using EasyWin.ViewModels;
using Wpf.Ui.Controls;

namespace EasyWin.Views;

public partial class RdpEditWindow : FluentWindow
{
    private readonly RdpProfile _profile;
    private readonly NetworkService _network;

    /// <summary>点击保存后包含编辑结果;取消时为 null。</summary>
    public RdpProfile? Result { get; private set; }

    public RdpEditWindow(RdpProfile profile, NetworkService network)
    {
        InitializeComponent();
        _profile = profile;
        _network = network;
        DataContext = RdpEditViewModel.From(profile);
        Title = string.IsNullOrEmpty(profile.Name) ? "新建连接" : "编辑连接";
        ContentRendered += (_, _) => NameBox.Focus();
        PasswordBox.Password = ((RdpEditViewModel)DataContext).Password ?? "";
        PasswordBox.PasswordChanged += (_, _) => ((RdpEditViewModel)DataContext).Password = PasswordBox.Password;
    }

    /// <summary>扫描本网段开放远程桌面的设备,结果显示在下拉列表中。</summary>
    private async void OnScanClick(object sender, RoutedEventArgs e)
    {
        if (!ScanButton.IsEnabled) return;
        ScanButton.IsEnabled = false;
        ScanList.ItemsSource = null;
        ScanStatusText.Text = "正在扫描本网段(约需几秒)…";
        ScanStatusText.Visibility = Visibility.Visible;
        ScanPopup.IsOpen = true;

        try
        {
            // 扫描端口:服务器栏带自定义端口就用它,否则 3389
            var (_, port) = RdpLauncher.ParseServer(ServerBox.Text);

            // WMI 枚举网卡较慢,放后台线程,避免弹窗打开时卡 UI
            var adapters = (await Task.Run(() => _network.GetAdapters()).ConfigureAwait(true))
                .Where(a => a.IsConnected && a.IpAddress != null && a.SubnetMask != null)
                .Select(a => (a.IpAddress!, a.SubnetMask!))
                .ToList();

            if (adapters.Count == 0)
            {
                ScanStatusText.Text = "未检测到已连接的网卡,无法扫描。";
                return;
            }

            var hosts = await LanScanner.ScanRdpAsync(adapters, port);

            if (hosts.Count == 0)
            {
                ScanStatusText.Text = $"未发现在 {port} 端口开放远程桌面的设备(可检查对方是否已开启远程桌面)。";
                return;
            }

            ScanStatusText.Text = $"发现 {hosts.Count} 台设备:";
            ScanList.ItemsSource = hosts;
        }
        catch (Exception ex)
        {
            Log.Error("扫描局域网失败", ex);
            ScanStatusText.Text = "扫描失败:" + ex.Message;
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }
    }

    private void OnScanItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ScanList.SelectedItem is LanHost host)
        {
            ServerBox.Text = host.Port == 3389 ? host.Ip : $"{host.Ip}:{host.Port}";
            ScanPopup.IsOpen = false;
        }
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var vm = (RdpEditViewModel)DataContext;

        if (string.IsNullOrWhiteSpace(vm.Name))
        {
            await Ui.AlertAsync("请填写连接名称", "连接名称不能为空。");
            return;
        }

        var server = vm.Server.Trim();
        if (server.Length == 0)
        {
            await Ui.AlertAsync("请填写服务器地址", "服务器地址不能为空。");
            return;
        }
        if (server.Any(char.IsWhiteSpace))
        {
            await Ui.AlertAsync("地址无效", "服务器地址不能包含空格。");
            return;
        }
        if (!string.IsNullOrWhiteSpace(vm.Mac) && !WolService.TryNormalizeMac(vm.Mac, out _, out _))
        {
            await Ui.AlertAsync("MAC 地址无效", "MAC 应为 12 位十六进制字符,可用 - : 或空格分隔。");
            return;
        }
        if ((vm.RememberPassword || !string.IsNullOrEmpty(vm.Password)) &&
            string.IsNullOrWhiteSpace(vm.UserName))
        {
            await Ui.AlertAsync("请填写用户名", "记住凭据需要同时填写用户名。");
            return;
        }

        vm.Password = PasswordBox.Password;
        vm.CopyTo(_profile);
        Result = _profile;
        DialogResult = true;
    }
}
