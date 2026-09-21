using CommunityToolkit.Mvvm.ComponentModel;
using EasyWin.Models;

namespace EasyWin.ViewModels;

/// <summary>远程桌面连接编辑窗口的绑定模型。</summary>
public partial class RdpEditViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _server = "";
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _mac = "";
    [ObservableProperty] private bool _rememberPassword;
    [ObservableProperty] private bool _fullScreen = true;
    [ObservableProperty] private string _widthText = "1280";
    [ObservableProperty] private string _heightText = "800";
    [ObservableProperty] private bool _adminSession;

    /// <summary>窗口模式时分辨率输入才可用。</summary>
    public bool IsWindowed => !FullScreen;

    /// <summary>密码明文仅在窗口生命周期内存在,保存时立即加密。</summary>
    public string? Password { get; set; }

    partial void OnFullScreenChanged(bool value) => OnPropertyChanged(nameof(IsWindowed));

    public static RdpEditViewModel From(RdpProfile profile) => new()
    {
        Name = profile.Name,
        Server = profile.Server,
        UserName = profile.UserName,
        Mac = profile.Mac,
        RememberPassword = profile.RememberPassword,
        FullScreen = profile.FullScreen,
        WidthText = profile.Width.ToString(),
        HeightText = profile.Height.ToString(),
        AdminSession = profile.AdminSession,
        Password = profile.GetPassword(),
    };

    public void CopyTo(RdpProfile profile)
    {
        profile.Name = Name.Trim();
        profile.Server = Server.Trim();
        profile.UserName = UserName.Trim();
        profile.Mac = Mac.Trim();
        profile.RememberPassword = RememberPassword;
        profile.FullScreen = FullScreen;
        _ = int.TryParse(WidthText, out var w);
        _ = int.TryParse(HeightText, out var h);
        profile.Width = Math.Clamp(w == 0 ? 1280 : w, 640, 7680);
        profile.Height = Math.Clamp(h == 0 ? 800 : h, 480, 4320);
        profile.AdminSession = AdminSession;
        profile.SetPassword(RememberPassword ? Password : null);
    }
}
