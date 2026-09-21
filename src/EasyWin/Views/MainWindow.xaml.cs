using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;
using EasyWin.Services;

namespace EasyWin.Views;

public partial class MainWindow : FluentWindow
{
    private const int ToastSeconds = 4;

    private bool _exitConfirmed;
    private bool _systemShutdown;
    private TrayService? _tray;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Toast.ShowRequested += OnToastRequested;
        Closed += OnClosed;

        // Windows 注销/关机时不做拦截,直接放行
        Application.Current.SessionEnding += (_, _) => _systemShutdown = true;

        // 托盘:双击/左键打开窗口,右键菜单快捷切换,退出走托盘菜单
        _tray = App.GetService<TrayService>();
        _tray.OpenRequested += ShowFromTray;
        _tray.ExitRequested += ExitFromTray;
    }

    /// <summary>点关闭按钮 = 隐藏到托盘(规则与托盘快捷切换需要后台常驻);真正退出走托盘菜单。</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_exitConfirmed || _systemShutdown)
            return; // 放行

        e.Cancel = true;
        Hide();
        _tray?.ShowMinimizedHint();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        _exitConfirmed = true;
        Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Toast.ShowRequested -= OnToastRequested;
        if (_tray != null)
        {
            _tray.OpenRequested -= ShowFromTray;
            _tray.ExitRequested -= ExitFromTray;
            _tray.Dispose();
            _tray = null;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.SetPageProviderService(new PageProvider());
        RootNavigation.Navigate(ResolveStartupPage());
    }

    /// <summary>支持 --page 网络与IP|配置方案|网络工具|系统设置|快捷启动|关于 直接跳转,默认仪表盘。</summary>
    private Type ResolveStartupPage()
    {
        var args = Environment.GetCommandLineArgs();
        var pageArg = args.FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase));
        if (pageArg == null) return typeof(DashboardPage);
        return pageArg.Substring("--page=".Length) switch
        {
            "网络与IP" or "network" => typeof(NetworkPage),
            "wifi" or "Wi-Fi" => typeof(WifiPage),
            "IP方案" or "profiles" => typeof(ProfilesPage),
            "远程桌面" or "rdp" => typeof(RdpPage),
            "网络工具" or "nettools" => typeof(NetToolsPage),
            "系统设置" or "tweaks" => typeof(TweaksPage),
            "快捷启动" or "launcher" => typeof(LauncherPage),
            "关于" or "about" => typeof(AboutPage),
            _ => typeof(DashboardPage),
        };
    }

    private void OnToastRequested(string message, ToastType type)
    {
        var (symbol, color) = type switch
        {
            ToastType.Success => ("Checkmark24", "#5CB87A"),
            ToastType.Warning => ("Warning24", "#E2B93B"),
            ToastType.Error => ("Dismiss24", "#E25B5B"),
            _ => ("Info24", "#5BA8E2"),
        };

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 6, 0, 0),
            MinWidth = 260,
            MaxWidth = 420,
            Opacity = 0,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new SymbolIcon { Symbol = (SymbolRegular)Enum.Parse(typeof(SymbolRegular), symbol), FontSize = 18, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), VerticalAlignment = VerticalAlignment.Center },
                    new System.Windows.Controls.TextBlock { Text = message, Margin = new Thickness(10, 0, 0, 0), FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 380, VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.White },
                }
            },
        };

        ToastHost.Items.Add(card);
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
        card.BeginAnimation(OpacityProperty, fadeIn);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ToastSeconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(240));
            fadeOut.Completed += (_, _) => ToastHost.Items.Remove(card);
            card.BeginAnimation(OpacityProperty, fadeOut);
        };
        timer.Start();
    }
}

/// <summary>为 NavigationView 提供缓存页面。</summary>
public class PageProvider : INavigationViewPageProvider
{
    public object? GetPage(Type pageType) => App.GetPage(pageType);
}
