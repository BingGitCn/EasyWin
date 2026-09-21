using System.Windows.Controls;
using EasyWin.ViewModels;

namespace EasyWin.Views;

public partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
        // 注意:DataContext 由 App 页面工厂在构造完成后赋值,这里不能缓存
        Loaded += (_, _) => (DataContext as DashboardViewModel)?.Start();
        Unloaded += (_, _) => (DataContext as DashboardViewModel)?.Stop();
    }
}
