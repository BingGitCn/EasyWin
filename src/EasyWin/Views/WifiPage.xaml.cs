using System.Windows;
using System.Windows.Controls;
using EasyWin.Services;

namespace EasyWin.Views;

public partial class WifiPage : Page
{
    public WifiPage()
    {
        InitializeComponent();
        // DataContext 由页面工厂在构造完成后赋值,接线放到 Loaded 里做
        Loaded += async (_, _) =>
        {
            if (DataContext is not ViewModels.WifiViewModel vm) return;
            vm.PromptWifiPassword = (ssid, auth) =>
            {
                var window = new WlanPasswordWindow(ssid, auth) { Owner = App.Current.MainWindow };
                window.ShowDialog();
                return Task.FromResult(window.Result);
            };
            await vm.RefreshAsync();
        };
    }
}
