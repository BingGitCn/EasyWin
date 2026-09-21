using System.Windows.Controls;

namespace EasyWin.Views;

public partial class RdpPage : Page
{
    public RdpPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ((ViewModels.RdpViewModel)DataContext).RefreshAsync();
    }
}
