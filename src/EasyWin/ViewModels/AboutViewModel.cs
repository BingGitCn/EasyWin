using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Appearance;
using EasyWin.Services;

namespace EasyWin.ViewModels;

/// <summary>主题选项(用于三态选择按钮)。</summary>
public record ThemeChoice(string Label, ThemeMode Mode);

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

    public static IReadOnlyList<ThemeChoice> ThemeChoices { get; } =
    [
        new("跟随系统", ThemeMode.System),
        new("浅色", ThemeMode.Light),
        new("深色", ThemeMode.Dark),
    ];

    [ObservableProperty]
    private ThemeChoice? _selectedTheme =
        ThemeChoices.FirstOrDefault(c => c.Mode == SettingsStore.Mode) ?? ThemeChoices[2];

    partial void OnSelectedThemeChanged(ThemeChoice? value)
    {
        if (value == null) return;
        SettingsStore.Mode = value.Mode;
        ApplyCurrentTheme();
    }

    /// <summary>三态按钮共用命令,参数为模式名(System/Light/Dark)。</summary>
    [RelayCommand]
    private void SetTheme(string? mode)
    {
        if (!Enum.TryParse<ThemeMode>(mode, ignoreCase: true, out var parsed)) return;
        SelectedTheme = ThemeChoices.First(c => c.Mode == parsed);
    }

    /// <summary>按当前设置应用主题(启动与切换共用)。</summary>
    public static void ApplyCurrentTheme()
    {
        ApplicationThemeManager.Apply(SettingsStore.IsDark ? ApplicationTheme.Dark : ApplicationTheme.Light);
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
