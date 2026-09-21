using System.Windows;
using EasyWin.Services;
using Wpf.Ui.Controls;

namespace EasyWin.Views;

public partial class WlanPasswordWindow : FluentWindow
{
    /// <summary>点击连接后:Item1=确认,Item2=密码(可空=用已保存配置)。</summary>
    public (bool confirmed, string? password) Result { get; private set; }

    public WlanPasswordWindow(string ssid, string auth)
    {
        InitializeComponent();
        Title = "连接 Wi-Fi";
        Ssid = ssid;
        AuthText.Text = string.IsNullOrWhiteSpace(auth) ? "" : $"加密方式:{auth}";
        Loaded += (_, _) => PasswordBox.Focus();
    }

    public string Ssid { get; }

    private async void OnConnect(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(password))
        {
            // 空密码 = 尝试用已保存的配置连接,需二次确认
            if (!await Ui.ConfirmAsync("密码为空", "没有输入密码,将尝试使用该网络已保存的配置连接。", "如果之前没连过这个网络,连接会失败。继续吗?"))
                return;
        }

        Result = (true, string.IsNullOrEmpty(password) ? null : password);
        DialogResult = true;
    }
}
