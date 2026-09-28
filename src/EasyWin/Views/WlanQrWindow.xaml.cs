using System.Windows;
using System.Windows.Media.Imaging;
using EasyWin.Services;
using Wpf.Ui.Controls;

namespace EasyWin.Views;

/// <summary>Wi-Fi 连接二维码窗口:扫码免密加入网络。</summary>
public partial class WlanQrWindow : FluentWindow
{
    public string Ssid { get; }

    public WlanQrWindow(string ssid, string? password)
    {
        InitializeComponent();
        Title = "Wi-Fi 二维码";
        Ssid = ssid;
        Loaded += (_, _) => GenerateAsync(password);
    }

    private async void GenerateAsync(string? password)
    {
        var png = await System.Threading.Tasks.Task.Run(() => WifiQrCodeService.GeneratePng(Ssid, password))
            .ConfigureAwait(true);
        var image = WifiQrCodeService.ToImage(png);
        if (image == null)
        {
            LoadingText.Text = "生成失败,请重试";
            return;
        }
        QrImage.Source = image;
        QrImage.Visibility = Visibility.Visible;
        LoadingText.Visibility = Visibility.Collapsed;
    }
}
