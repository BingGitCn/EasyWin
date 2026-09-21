using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        // 滚轮兜底:页面内容在某些焦点/模板状态下收不到滚轮事件(只能拖右侧滑块),
        // 在窗口路由末端兜底——事件未被处理时手动滚动光标下最近的 ScrollViewer
        AddHandler(UIElement.MouseWheelEvent, new RoutedEventHandler(OnWheelFallback), handledEventsToo: true);

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
        RootNavigation.Navigated += OnPageNavigated;
        RootNavigation.SetPageProviderService(new PageProvider());
        RootNavigation.Navigate(ResolveStartupPage());

        // 跟随系统模式时监听 Windows 深浅色切换,实时换肤
        if (Services.SettingsStore.Mode == Services.ThemeMode.System)
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

        // 开机自启(--tray)时不弹主窗口,直接常驻托盘
        if (Environment.GetCommandLineArgs().Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase)))
        {
            Hide();
            _tray?.ShowMinimizedHint();
        }
    }

    /// <summary>页面切入过渡:淡入 + 轻微上移,让导航切换不生硬。</summary>
    private void OnPageNavigated(Wpf.Ui.Controls.NavigationView sender, Wpf.Ui.Controls.NavigatedEventArgs args)
    {
        if (args.Page is System.Windows.Controls.Page page)
            PlayEntrance(page);
    }

    /// <summary>滚轮兜底:正常情况下内容 ScrollViewer 自己处理滚轮(Handled),
    /// 若事件一路冒泡到窗口仍未被处理,则手动滚动最近的 ScrollViewer。</summary>
    private void OnWheelFallback(object sender, RoutedEventArgs args)
    {
        if (args is not MouseWheelEventArgs e || e.Handled) return;
        var sv = FindAncestorScrollViewer(e.OriginalSource as DependencyObject)
                 ?? FindFirstScrollViewer(RootNavigation);
        if (sv == null || sv.ScrollableHeight <= 0) return; // 没有可滚动的空间,不吞事件
        var target = sv.VerticalOffset - e.Delta;           // 向上滚 delta 为正 → 向上偏移
        if (target < 0 || target > sv.ScrollableHeight) return; // 已到边界,不吞事件
        sv.ScrollToVerticalOffset(target);
        e.Handled = true;
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject? node)
    {
        while (node != null)
        {
            if (node is ScrollViewer sv) return sv;
            if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
                node = VisualTreeHelper.GetParent(node);
            else
                node = LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    private static ScrollViewer? FindFirstScrollViewer(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindFirstScrollViewer(child);
            if (found != null) return found;
        }
        return null;
    }

    private static void PlayEntrance(FrameworkElement page)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new TranslateTransform(0, 14);
        page.RenderTransform = slide;
        page.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
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

        var content = new StackPanel { Orientation = Orientation.Horizontal };

        content.Children.Add(new SymbolIcon { Symbol = (SymbolRegular)Enum.Parse(typeof(SymbolRegular), symbol), FontSize = 18, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new System.Windows.Controls.TextBlock { Text = message, Margin = new Thickness(10, 0, 0, 0), FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 380, VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.White });

        // 关闭按钮:点卡片任意处或点 × 都能立即关掉
        content.Children.Add(new Wpf.Ui.Controls.Button
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Dismiss24, FontSize = 14 },
            Appearance = ControlAppearance.Transparent,
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 10, 10),
            Margin = new Thickness(0, 6, 0, 0),
            MinWidth = 260,
            MaxWidth = 460,
            Opacity = 0,
            Cursor = Cursors.Hand,
            Child = content,
        };

        ToastHost.Items.Add(card);
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
        card.BeginAnimation(OpacityProperty, fadeIn);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ToastSeconds) };
        void Dismiss()
        {
            timer.Stop();
            card.IsHitTestVisible = false;
            var fadeOut = new DoubleAnimation(card.Opacity, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (_, _) => ToastHost.Items.Remove(card);
            card.BeginAnimation(OpacityProperty, fadeOut);
        }
        timer.Tick += (_, _) => Dismiss();
        timer.Start();
        card.MouseLeftButtonDown += (_, _) => Dismiss();
        ((Wpf.Ui.Controls.Button)content.Children[^1]).Click += (_, _) => Dismiss();
    }
}

/// <summary>为 NavigationView 提供缓存页面。</summary>
public class PageProvider : INavigationViewPageProvider
{
    public object? GetPage(Type pageType) => App.GetPage(pageType);
}
