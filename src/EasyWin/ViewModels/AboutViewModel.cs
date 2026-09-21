using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Appearance;
using EasyWin.Services;

namespace EasyWin.ViewModels;

/// <summary>关于页:版本信息、主题设置、数据目录。</summary>
public partial class AboutViewModel : ObservableObject
{
    public string VersionText
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public bool IsElevated { get; } = CheckElevated();

    public string StorePath => AppPaths.ProfilesPath;

    [ObservableProperty] private bool _isDarkTheme = SettingsStore.Theme;

    partial void OnIsDarkThemeChanged(bool value)
    {
        SettingsStore.Theme = value;
        ApplicationThemeManager.Apply(value ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Toast.Error("打开目录失败:" + ex.Message);
        }
    }

    [RelayCommand]
    private void OpenStoreFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{StorePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Toast.Error("打开文件失败:" + ex.Message);
        }
    }

    private static bool CheckElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
