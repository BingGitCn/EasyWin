using System.Windows.Controls;

namespace EasyWin.Views;

public partial class ProfilesPage : Page
{
    public ProfilesPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ((ViewModels.ProfilesViewModel)DataContext).RefreshAsync();
    }
}
