using System.Collections.Frozen;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EasyWin.Models;
using EasyWin.Services;
using EasyWin.ViewModels;
using EasyWin.Views;

namespace EasyWin;

public partial class App : Application
{
    private static Mutex? _mutex;

    private readonly Dictionary<Type, object> _services = [];

    protected override async void OnStartup(StartupEventArgs e)
    {
        // netsh 在中文系统输出 GBK,需要注册代码页
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // 单实例
        _mutex = new Mutex(true, "EasyWin_SingleInstance_9F3A2C", out var createdNew);
        if (!createdNew)
        {
            await Ui.AlertAsync("EasyWin", "EasyWin 已经在运行中。");
            Shutdown();
            return;
        }

        // 主题:--theme=light|dark,默认记住上次选择,首次为深色
        var themeArg = e.Args.FirstOrDefault(a => a.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase));
        var saved = SettingsStore.Theme;
        var theme = themeArg != null
            ? themeArg.Substring("--theme=".Length).Equals("light", StringComparison.OrdinalIgnoreCase)
            : saved;
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
            theme ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);
        Log.Info($"主题应用: arg={themeArg ?? "(无)"} saved={saved} -> {Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme()}");

        base.OnStartup(e);
    }

    private async void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("未处理异常", e.Exception);
        e.Handled = true;
        await Ui.AlertAsync("EasyWin 发生错误", e.Exception.Message);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }


    /// <summary>获取(或创建)单例服务。</summary>
    public static T GetService<T>() where T : class
    {
        var app = (App)Current;
        if (app._services.TryGetValue(typeof(T), out var existing))
            return (T)existing;

        var instance = Create(typeof(T));
        app._services[typeof(T)] = instance;
        return (T)instance;
    }

    private static object Create(Type type) => type switch
    {
        _ when type == typeof(NetworkService) => new NetworkService(),
        _ when type == typeof(ProfileStore) => new ProfileStore(),
        _ when type == typeof(RdpProfileStore) => new RdpProfileStore(),
        _ when type == typeof(SystemInfoService) => new SystemInfoService(),
        _ when type == typeof(NetworkViewModel) => new NetworkViewModel(GetService<NetworkService>()),
        _ when type == typeof(ProfilesViewModel) => new ProfilesViewModel(GetService<NetworkService>(), GetService<ProfileStore>()),
        _ when type == typeof(RdpViewModel) => new RdpViewModel(GetService<RdpProfileStore>(), GetService<NetworkService>()),
        _ when type == typeof(WifiViewModel) => new WifiViewModel(GetService<NetworkService>()),
        _ when type == typeof(NetToolsViewModel) => new NetToolsViewModel(GetService<NetworkService>()),
        _ when type == typeof(TweaksViewModel) => new TweaksViewModel(GetService<NetworkService>()),
        _ when type == typeof(DashboardViewModel) => new DashboardViewModel(GetService<SystemInfoService>(), GetService<NetworkService>()),
        _ when type == typeof(LauncherViewModel) => new LauncherViewModel(),
        _ when type == typeof(AboutViewModel) => new AboutViewModel(),
        _ => throw new InvalidOperationException($"未注册的服务类型: {type.Name}")
    };

    private static readonly FrozenDictionary<Type, Func<Page>> _pageFactories = new Dictionary<Type, Func<Page>>
    {
        [typeof(DashboardPage)] = () => new DashboardPage { DataContext = GetService<DashboardViewModel>() },
        [typeof(NetworkPage)] = () => new NetworkPage { DataContext = GetService<NetworkViewModel>() },
        [typeof(ProfilesPage)] = () => new ProfilesPage { DataContext = GetService<ProfilesViewModel>() },
        [typeof(RdpPage)] = () => new RdpPage { DataContext = GetService<RdpViewModel>() },
        [typeof(WifiPage)] = () => new WifiPage { DataContext = GetService<WifiViewModel>() },
        [typeof(NetToolsPage)] = () => new NetToolsPage { DataContext = GetService<NetToolsViewModel>() },
        [typeof(TweaksPage)] = () => new TweaksPage { DataContext = GetService<TweaksViewModel>() },
        [typeof(LauncherPage)] = () => new LauncherPage { DataContext = GetService<LauncherViewModel>() },
        [typeof(AboutPage)] = () => new AboutPage { DataContext = GetService<AboutViewModel>() },
    }.ToFrozenDictionary();

    private static readonly Dictionary<Type, Page> _pageCache = [];

    /// <summary>获取(或创建)缓存的页面实例。</summary>
    public static Page? GetPage(Type pageType)
    {
        if (_pageCache.TryGetValue(pageType, out var cached))
            return cached;

        if (_pageFactories.TryGetValue(pageType, out var factory))
        {
            var page = factory();
            _pageCache[pageType] = page;
            return page;
        }

        return null;
    }
}
