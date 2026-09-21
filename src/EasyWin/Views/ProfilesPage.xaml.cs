using System.Windows;
using System.Windows.Controls;

namespace EasyWin.Views;

public partial class ProfilesPage : Page
{
    public ProfilesPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ((ViewModels.ProfilesViewModel)DataContext).RefreshAsync();
    }

    /// <summary>规则开关切换(ToggleSwitch 的 Click 只在用户操作时触发),把新状态落盘。</summary>
    private void OnRuleToggled(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Models.AutomationRule rule
            && DataContext is ViewModels.ProfilesViewModel vm)
        {
            vm.PersistRule(rule);
        }
    }
}
