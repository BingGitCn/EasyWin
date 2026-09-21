using System.Windows;
using System.Windows.Controls;
using EasyWin.Services;

namespace EasyWin.Views;

public partial class TweaksPage : Page
{
    public TweaksPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ((ViewModels.TweaksViewModel)DataContext).RefreshAsync();
    }
}
