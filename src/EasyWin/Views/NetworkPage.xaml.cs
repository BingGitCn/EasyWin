using System.Windows.Controls;

namespace EasyWin.Views;

public partial class NetworkPage : Page
{
    public NetworkPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ((ViewModels.NetworkViewModel)DataContext).RefreshAsync();
    }
}
