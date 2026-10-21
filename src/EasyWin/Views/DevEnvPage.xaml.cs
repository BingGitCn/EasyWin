using System.Windows.Controls;

namespace EasyWin.Views;

public partial class DevEnvPage : Page
{
    public DevEnvPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ((ViewModels.DevEnvViewModel)DataContext).RefreshAsync();
    }
}
