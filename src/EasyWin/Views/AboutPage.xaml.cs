using System.Diagnostics;
using System.Windows.Navigation;

namespace EasyWin.Views;

public partial class AboutPage : System.Windows.Controls.Page
{
    public AboutPage()
    {
        InitializeComponent();
    }

    /// <summary>点击仓库超链接,用默认浏览器打开。</summary>
    private void OnRepoNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true,
        });
        e.Handled = true;
    }
}
