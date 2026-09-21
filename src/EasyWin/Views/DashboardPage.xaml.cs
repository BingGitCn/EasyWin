using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyWin.Models;
using EasyWin.ViewModels;

namespace EasyWin.Views;

public partial class DashboardPage : System.Windows.Controls.Page
{
    public DashboardPage()
    {
        InitializeComponent();
        // 注意:DataContext 由 App 页面工厂在构造完成后赋值,这里不能缓存
        Loaded += (_, _) => (DataContext as DashboardViewModel)?.Start();
        Unloaded += (_, _) => (DataContext as DashboardViewModel)?.Stop();
    }

    private DashboardViewModel? Vm => DataContext as DashboardViewModel;

    /// <summary>右键菜单:打开磁盘。</summary>
    private void OnDiskOpenClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DiskInfo disk)
            Vm?.OpenDiskCommand.Execute(disk);
    }

    /// <summary>右键菜单:安全弹出(仅可移动设备显示)。</summary>
    private async void OnDiskEjectClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DiskInfo disk)
            await Vm?.EjectDiskCommand.ExecuteAsync(disk)!;
    }
}
