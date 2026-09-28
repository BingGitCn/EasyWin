using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWin.Models;
using EasyWin.Services;

namespace EasyWin.ViewModels;

/// <summary>系统设置页的一张调整卡片。</summary>
public partial class TweakCardViewModel : ObservableObject
{
    private readonly Func<bool> _check;
    private readonly Func<bool, Task<string?>> _apply;
    private readonly Action _refresh;

    public TweakCardViewModel(string title, string description, string symbol,
        Func<bool> check, Func<bool, Task<string?>> apply, string applyText, string restoreText, Action refresh,
        Func<Task>? promptAfterApply = null, bool risky = false)
    {
        Title = title;
        Description = description;
        Symbol = symbol;
        _check = check;
        _apply = apply;
        ApplyText = applyText;
        RestoreText = restoreText;
        _refresh = refresh;
        PromptAfterApply = promptAfterApply;
        IsRisky = risky;
        // 初检不在这里做:构造在 UI 线程,几十张卡片的注册表检测会拖慢进页;
        // 页面 Loaded 会统一走 RefreshAsync 补齐状态
    }

    public string Title { get; }
    public string Description { get; }
    public string Symbol { get; }
    public string ApplyText { get; }
    public string RestoreText { get; }

    /// <summary>高风险调整(关闭安全防护类),应用前需二次确认,卡片带「高危」角标。</summary>
    public bool IsRisky { get; }

    /// <summary>应用成功后的附加提示(如去箭头后询问是否重启资源管理器)。</summary>
    public Func<Task>? PromptAfterApply { get; }

    [ObservableProperty] private bool _isApplied;
    [ObservableProperty] private string _statusText = "检测中…";
    [ObservableProperty] private bool _isBusy;

    /// <summary>忙碌时禁用按钮。</summary>
    public bool IsNotBusy => !IsBusy;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    /// <summary>主按钮文案:未生效显示“应用文案”,已生效显示“恢复文案”。</summary>
    public string ActionText => IsApplied ? RestoreText : ApplyText;

    public void ReloadState(bool silent = false)
    {
        try
        {
            IsApplied = _check();
            StatusText = IsApplied ? "已启用" : "未启用";
        }
        catch (Exception ex)
        {
            Log.Error($"检测「{Title}」状态失败", ex);
            StatusText = "检测失败";
        }
        OnPropertyChanged(nameof(ActionText));
    }

    [RelayCommand]
    public async Task ToggleAsync()
    {
        if (IsBusy) return;
        var target = !IsApplied;
        if (target && IsRisky && !await Ui.ConfirmAsync($"高危调整:{Title}",
                "此调整会关闭系统安全防护,确定继续吗?", Description).ConfigureAwait(true)) return;
        IsBusy = true;
        try
        {
            var warning = await Task.Run(() => _apply.Invoke(target)).ConfigureAwait(true);
            _refresh();
            ReloadState();
            if (IsApplied == target)
            {
                Toast.Success(warning == null ? $"「{Title}」已{(IsApplied ? "启用" : "恢复")}" : $"「{Title}」已生效:{warning}");
                if (IsApplied && PromptAfterApply != null)
                    await PromptAfterApply().ConfigureAwait(true);
            }
            else
                Toast.Warning($"「{Title}」状态异常:{warning ?? "应用后检测未通过,建议重试"}");
        }
        catch (Exception ex)
        {
            Log.Error($"应用「{Title}」失败", ex);
            Toast.Error($"「{Title}」操作失败:{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>系统设置页:更新、资源管理器、任务栏、隐私、系统与安全、电源计划。</summary>
public partial class TweaksViewModel : ObservableObject
{
    private readonly NetworkService _network;
    private readonly PowerPlanAutoSwitchService _powerAuto;

    public TweaksViewModel(NetworkService network, PowerPlanAutoSwitchService powerAuto)
    {
        _network = network;
        _powerAuto = powerAuto;

        // ---------- 系统更新 ----------
        UpdateSection =
        [
            new TweakCardViewModel("禁用 Windows 自动更新",
                "组策略 NoAutoUpdate + 禁用 wuauserv + 禁用 WaaSMedicSvc(防自动恢复)。建议定期恢复更新以获取安全补丁。",
                "ArrowClockwise24", TweakService.IsUpdateDisabled,
                async target => await Task.Run(() => TweakService.SetUpdateDisabled(target)),
                "禁用更新", "恢复更新",
                () => { }),
        ];

        // ---------- 资源管理器 ----------
        var explorer = new List<TweakCardViewModel>
        {
            new("去除快捷方式小箭头",
                "修改 Shell Icons 注册表项,生效需重启资源管理器。",
                "Link24", TweakService.IsShortcutArrowHidden,
                async target => { TweakService.SetShortcutArrowHidden(target); return (string?)null; },
                "去箭头", "恢复箭头",
                () => { }, promptAfterApply: AskRestartExplorerAsync),
            new("隐藏「快捷方式」字样",
                "新建快捷方式时不再自动添加「- 快捷方式」后缀。",
                "Edit24", TweakService.IsShortcutPrefixHidden,
                async target => { TweakService.SetShortcutPrefixHidden(target); return (string?)null; },
                "隐藏字样", "恢复字样",
                () => { }),
            new("显示文件扩展名",
                "资源管理器显示已知文件类型的扩展名(如 .txt / .jpg),避免伪装成文档的可执行文件误导。",
                "Tag24", TweakService.IsFileExtensionShown,
                async target => { TweakService.SetFileExtensionShown(target); return (string?)null; },
                "显示扩展名", "隐藏扩展名",
                () => { }),
            new("显示隐藏文件",
                "资源管理器显示隐藏属性的文件与文件夹。",
                "Eye24", TweakService.IsHiddenFilesShown,
                async target => { TweakService.SetHiddenFilesShown(target); return (string?)null; },
                "显示隐藏", "隐藏隐藏项",
                () => { }),
            new("隐藏「3D 对象」文件夹",
                "从此电脑中移除几乎用不到的「3D 对象」文件夹,生效需重启资源管理器。",
                "Folder24", TweakService.Is3DObjectsHidden,
                async target => { TweakService.Set3DObjectsHidden(target); return (string?)null; },
                "隐藏 3D", "恢复 3D",
                () => { }, promptAfterApply: AskRestartExplorerAsync),
            new("右键「管理员取得所有权」",
                "在文件/文件夹右键菜单中添加一键取得所有权的入口(takeown + icacls)。",
                "Key24", TweakService.IsTakeOwnershipMenuEnabled,
                async target => { TweakService.SetTakeOwnershipMenu(target); return (string?)null; },
                "添加菜单", "移除菜单",
                () => { }),
            new("右键「在此处打开 CMD」",
                "在文件夹背景右键菜单中添加命令行入口,自动定位到当前目录。",
                "Code24", TweakService.IsCmdHereMenuEnabled,
                async target => { TweakService.SetCmdHereMenu(target); return (string?)null; },
                "添加菜单", "移除菜单",
                () => { }),
            new("默认打开「此电脑」",
                "资源管理器启动时直接进入此电脑,而不是快速访问。(微软开发机配置同款)",
                "FolderOpen24", TweakService.IsLaunchToThisPc,
                async target => { TweakService.SetLaunchToThisPc(target); return (string?)null; },
                "打开此电脑", "恢复快速访问",
                () => { }),
            new("标题栏显示完整路径",
                "资源管理器窗口标题显示当前文件夹的完整路径,复制路径、分辨同名文件夹更方便。",
                "LinkMultiple24", TweakService.IsFullPathInTitleBar,
                async target => { TweakService.SetFullPathInTitleBar(target); return (string?)null; },
                "显示全路径", "恢复短标题",
                () => { }),
            new("精简快速访问与推广提示",
                "关闭快速访问的常用文件夹/最近文件/云文件推荐,以及资源管理器里的 OneDrive 同步推广提示。",
                "Broom24", TweakService.IsQuickAccessLean,
                async target => { TweakService.SetQuickAccessLean(target); return (string?)null; },
                "精简快速访问", "恢复快速访问",
                () => { }),
        };
        if (TweakService.IsWin11)
        {
            explorer.Insert(4, new TweakCardViewModel("恢复 Win10 经典右键菜单",
                "Win11 新菜单换回 Win10 完整版(一次显示全部菜单项),生效需重启资源管理器。",
                "List24", TweakService.IsClassicContextMenuEnabled,
                async target => { TweakService.SetClassicContextMenu(target); return (string?)null; },
                "经典菜单", "换回 Win11",
                () => { }, promptAfterApply: AskRestartExplorerAsync));
        }
        ExplorerSection = new ObservableCollection<TweakCardViewModel>(explorer);

        // ---------- 任务栏与开始菜单 ----------
        var taskbar = new List<TweakCardViewModel>
        {
            new("任务栏时钟显示秒数",
                "任务栏时钟以「时:分:秒」显示,写入后广播设置,未生效时可重启资源管理器。",
                "Clock24", TweakService.IsClockSecondsShown,
                async target => { TweakService.SetClockSecondsShown(target); return (string?)null; },
                "显示秒数", "隐藏秒数",
                () => { }),
            new("隐藏任务栏搜索框",
                "隐藏任务栏上的搜索框/搜索按钮,还你干净的任务栏。",
                "Search24", TweakService.IsTaskbarSearchHidden,
                async target => { TweakService.SetTaskbarSearchHidden(target); return (string?)null; },
                "隐藏搜索", "恢复搜索",
                () => { }),
            new("隐藏「任务视图」按钮",
                "隐藏任务栏上的任务视图(多桌面切换)按钮。",
                "SquareMultiple24", TweakService.IsTaskViewHidden,
                async target => { TweakService.SetTaskViewHidden(target); return (string?)null; },
                "隐藏任务视图", "恢复任务视图",
                () => { }),
            new("关闭开始菜单「推荐项目」",
                "不再在开始菜单显示推荐的应用/文件、推广内容与账户通知。",
                "MegaphoneLoud24", TweakService.IsStartRecommendationsHidden,
                async target => { TweakService.SetStartRecommendationsHidden(target); return (string?)null; },
                "关闭推荐", "恢复推荐",
                () => { }),
            new("关闭搜索亮点",
                "搜索框不再轮播每日热点图片与推荐内容(微软开发机配置同款)。",
                "Highlight24", TweakService.IsSearchHighlightsOff,
                async target => { TweakService.SetSearchHighlightsOff(target); return (string?)null; },
                "关闭亮点", "恢复亮点",
                () => { }),
        };
        if (TweakService.IsWin11)
        {
            taskbar.Insert(0, new TweakCardViewModel("任务栏图标靠左",
                "Win11 默认居中的任务栏图标改回 Win10 式靠左排列。",
                "AlignLeft24", TweakService.IsTaskbarLeft,
                async target => { TweakService.SetTaskbarLeft(target); return (string?)null; },
                "靠左对齐", "居中对齐",
                () => { }));
            taskbar.Add(new TweakCardViewModel("隐藏「小组件」按钮",
                "隐藏任务栏左侧的天气/资讯小组件入口。",
                "WeatherSunny24", TweakService.IsWidgetsHidden,
                async target => { TweakService.SetWidgetsHidden(target); return (string?)null; },
                "隐藏小组件", "恢复小组件",
                () => { }));
            taskbar.Add(new TweakCardViewModel("隐藏「聊天 / Copilot」按钮",
                "隐藏任务栏上的 Teams 聊天与 Copilot 按钮(不同版本显示其一)。",
                "Chat24", TweakService.IsChatCopilotHidden,
                async target => { TweakService.SetChatCopilotHidden(target); return (string?)null; },
                "隐藏按钮", "恢复按钮",
                () => { }));
        }
        if (TweakService.IsWin11_23H2)
        {
            taskbar.Add(new TweakCardViewModel("任务栏右键「结束任务」",
                "在任务栏图标右键菜单中直接结束进程,不用再打开任务管理器(微软开发机配置同款)。",
                "Dismiss24", TweakService.IsTaskbarEndTaskEnabled,
                async target => { TweakService.SetTaskbarEndTask(target); return (string?)null; },
                "启用结束任务", "移除结束任务",
                () => { }));
        }
        TaskbarSection = new ObservableCollection<TweakCardViewModel>(taskbar);

        // ---------- 隐私 ----------
        PrivacySection =
        [
            new TweakCardViewModel("关闭遥测与诊断数据",
                "组策略 AllowTelemetry=0 + 禁用 DiagTrack/dmwappushservice,Windows 不再上传使用数据。",
                "CloudArrowUp24", TweakService.IsTelemetryDisabled,
                async target => await Task.Run(() => TweakService.SetTelemetryDisabled(target)),
                "关闭遥测", "恢复遥测",
                () => { }),
            new("禁用广告 ID",
                "应用无法通过广告标识符关联你的使用习惯,系统与应用内个性化广告随之减少。",
                "Target24", TweakService.IsAdvertisingIdDisabled,
                async target => { TweakService.SetAdvertisingIdDisabled(target); return (string?)null; },
                "禁用广告 ID", "恢复广告 ID",
                () => { }),
            new("禁用位置跟踪",
                "组策略禁用系统定位服务,应用无法获取地理位置。",
                "Location24", TweakService.IsLocationTrackingDisabled,
                async target => { TweakService.SetLocationTrackingDisabled(target); return (string?)null; },
                "禁用定位", "恢复定位",
                () => { }),
            new("禁用活动历史记录",
                "不采集、不上传应用与浏览活动时间线(EnableActivityFeed=0)。",
                "History24", TweakService.IsActivityHistoryDisabled,
                async target => { TweakService.SetActivityHistoryDisabled(target); return (string?)null; },
                "禁用活动历史", "恢复活动历史",
                () => { }),
            new("禁用 Cortana",
                "组策略关闭 Cortana 语音助手与其数据收集。",
                "Mic24", TweakService.IsCortanaDisabled,
                async target => { TweakService.SetCortanaDisabled(target); return (string?)null; },
                "禁用 Cortana", "恢复 Cortana",
                () => { }),
            new("禁用错误报告",
                "不再向微软发送 Windows 错误报告(WER)。",
                "Bug24", TweakService.IsErrorReportingDisabled,
                async target => { TweakService.SetErrorReportingDisabled(target); return (string?)null; },
                "禁用报告", "恢复报告",
                () => { }),
            new("搜索仅显示本地结果",
                "开始菜单搜索禁用 Bing 网页建议,输入内容不再发送到微软服务器(建议重启资源管理器生效)。",
                "DocumentSearch24", TweakService.IsWebSearchSuggestionsDisabled,
                async target => { TweakService.SetWebSearchSuggestionsDisabled(target); return (string?)null; },
                "仅本地搜索", "恢复网页搜索",
                () => { }),
            new("勿扰模式(关闭全部通知)",
                "关闭所有应用的横幅通知,专注不被打断;恢复即还原通知总开关。",
                "AlertOff24", TweakService.IsNotificationsOff,
                async target => { TweakService.SetNotificationsOff(target); return (string?)null; },
                "开启勿扰", "恢复通知",
                () => { }),
        ];

        // ---------- 系统与安全 ----------
        var system = new List<TweakCardViewModel>
        {
            new TweakCardViewModel("启用远程桌面",
                "允许其他设备通过远程桌面连接本机(家庭版不支持作为被控端),开启时自动放行防火墙 RDP 入站规则。",
                "Desktop24", TweakService.IsRemoteDesktopEnabled,
                async target => { await TweakService.SetRemoteDesktopAsync(target); return (string?)null; },
                "开启远程", "关闭远程",
                () => { }),
            new TweakCardViewModel("视觉效果设为最佳性能",
                "关闭窗口动画、阴影等视觉效果,低配机更流畅;重启资源管理器后完全生效。",
                "Flash24", TweakService.IsBestPerformance,
                async target => { TweakService.SetBestPerformance(target); return (string?)null; },
                "最佳性能", "恢复默认",
                () => { }),
            new TweakCardViewModel("系统深色模式",
                "一键切换 Windows 系统与应用为深色(应用+系统双开关),随写随生效。",
                "DarkTheme24", TweakService.IsSystemDarkMode,
                async target => { TweakService.SetSystemDarkMode(target); return (string?)null; },
                "切换深色", "切换浅色",
                () => { }),
            new TweakCardViewModel("禁用休眠与快速启动",
                "powercfg /h off,同时关闭基于休眠的「快速启动」,适合双系统/频繁重启的机器,并可释放 hiberfil.sys 空间。",
                "Moon24", TweakService.IsHibernateDisabled,
                async target => { await TweakService.SetHibernateDisabledAsync(target); return (string?)null; },
                "禁用休眠", "恢复休眠",
                () => { }),
            new TweakCardViewModel("关闭自动播放",
                "插入 U 盘/移动硬盘不再自动运行,切断 Autorun 病毒传播途径。",
                "Play24", TweakService.IsAutoplayDisabled,
                async target => { TweakService.SetAutoplayDisabled(target); return (string?)null; },
                "关闭自动播放", "开启自动播放",
                () => { }),
            new TweakCardViewModel("开启防火墙",
                "域/专用/公用全部配置文件的防火墙。仅建议在受信任的内网环境临时关闭,用完记得开回来!",
                "Shield24", TweakService.IsFirewallEnabled,
                async target => { await TweakService.SetFirewallAsync(target); return (string?)null; },
                "开启防火墙", "关闭防火墙",
                () => { }, risky: true),
            new TweakCardViewModel("禁用 SmartScreen",
                "关闭应用与下载文件的安全筛选,运行陌生程序不再被拦截。有安全风险,一般不建议关闭!",
                "ShieldCheckmark24", TweakService.IsSmartScreenDisabled,
                async target => { TweakService.SetSmartScreenDisabled(target); return (string?)null; },
                "禁用 SmartScreen", "恢复 SmartScreen",
                () => { }, risky: true),
            new TweakCardViewModel("禁用内存完整性",
                "关闭基于虚拟化的 HVCI 内核完整性保护,部分老驱动/游戏反作弊兼容性更好,重启后生效。有安全风险!",
                "LockClosed24", TweakService.IsMemoryIntegrityDisabled,
                async target => { TweakService.SetMemoryIntegrityDisabled(target); return (string?)null; },
                "禁用内存完整性", "恢复内存完整性",
                () => { }, risky: true),
            new TweakCardViewModel("禁用系统还原",
                "组策略 DisableSR=1 关闭系统还原点功能并腾出磁盘空间。关闭后将无法用还原点回滚,慎用!",
                "ArrowUndo24", TweakService.IsSystemRestoreDisabled,
                async target => { TweakService.SetSystemRestoreDisabled(target); return (string?)null; },
                "禁用系统还原", "恢复系统还原",
                () => { }, risky: true),
        };
        system.Add(new TweakCardViewModel("开发者模式",
            "允许侧加载应用、创建符号链接等开发特性(AllowDevelopmentWithoutDevLicense=1,微软开发机配置同款)。",
            "Code24", TweakService.IsDeveloperModeEnabled,
            async target => { TweakService.SetDeveloperMode(target); return (string?)null; },
            "启用开发模式", "关闭开发模式",
            () => { }));
        system.Add(new TweakCardViewModel("启用长路径",
            "解除 Win32 API 260 字符路径长度限制(LongPathsEnabled=1),深度目录的项目/解压不再报错,重启生效。",
            "ArrowAutofitWidth24", TweakService.IsLongPathsEnabled,
            async target => { TweakService.SetLongPaths(target); return (string?)null; },
            "启用长路径", "恢复默认限制",
            () => { }));
        if (TweakService.IsWin11_24H2)
        {
            system.Add(new TweakCardViewModel("启用 Win11 Sudo",
                "在终端里直接用 sudo 提权执行命令(内联模式,微软开发机配置同款;可用 sudo config 切换窗口模式)。",
                "ArrowExpand24", TweakService.IsSudoEnabled,
                async target => { TweakService.SetSudo(target); return (string?)null; },
                "启用 Sudo", "关闭 Sudo",
                () => { }));
        }
        SystemSection = new ObservableCollection<TweakCardViewModel>(system);
    }

    public ObservableCollection<TweakCardViewModel> UpdateSection { get; }
    public ObservableCollection<TweakCardViewModel> ExplorerSection { get; }
    public ObservableCollection<TweakCardViewModel> TaskbarSection { get; }
    public ObservableCollection<TweakCardViewModel> PrivacySection { get; }
    public ObservableCollection<TweakCardViewModel> SystemSection { get; }

    public ObservableCollection<PowerPlan> PowerPlans { get; } = [];

    [ObservableProperty] private PowerPlan? _activePlan;
    [ObservableProperty] private bool _isBusy;

    // ---------------- 电源计划自动切换 ----------------

    /// <summary>只 load 一次作为初值;之后由 RefreshAsync 按 PowerPlans 解析,避免打开页面就触发写入。</summary>
    [ObservableProperty] private bool _powerAutoEnabled = SettingsStore.PowerAutoEnabled;

    [ObservableProperty] private string? _acPlanGuid = SettingsStore.AcPlanGuid;
    [ObservableProperty] private string? _batteryPlanGuid = SettingsStore.BatteryPlanGuid;

    partial void OnPowerAutoEnabledChanged(bool value)
    {
        SettingsStore.PowerAutoEnabled = value;
        if (value) _ = _powerAuto.CheckNowAsync(); // 开启后立即按当前供电状态生效
    }

    partial void OnAcPlanGuidChanged(string? value)
    {
        if (value == null) return;
        SettingsStore.AcPlanGuid = value;
        _ = _powerAuto.CheckNowAsync(); // 选择后立即按当前供电状态生效
    }

    partial void OnBatteryPlanGuidChanged(string? value)
    {
        if (value == null) return;
        SettingsStore.BatteryPlanGuid = value;
        _ = _powerAuto.CheckNowAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var plans = await TweakService.GetPowerPlansAsync().ConfigureAwait(true);
        PowerPlans.Clear();
        foreach (var plan in plans) PowerPlans.Add(plan);
        ActivePlan = plans.FirstOrDefault(p => p.IsActive);
        foreach (var card in UpdateSection.Concat(ExplorerSection).Concat(TaskbarSection)
                     .Concat(PrivacySection).Concat(SystemSection))
            card.ReloadState();
    }

    [RelayCommand]
    private async Task SetPowerPlanAsync(PowerPlan? plan)
    {
        if (plan == null || IsBusy) return;
        IsBusy = true;
        try
        {
            if (plan.Name.Contains("高性能") || string.Equals(plan.Guid, "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", StringComparison.OrdinalIgnoreCase))
                await TweakService.ActivateHighPerformanceAsync().ConfigureAwait(true);
            else
                await TweakService.SetPowerPlanAsync(plan.Guid).ConfigureAwait(true);

            await RefreshAsync().ConfigureAwait(true);
            Toast.Success($"电源计划已切换为「{plan.Name}」");
        }
        catch (Exception ex)
        {
            Log.Error("切换电源计划失败", ex);
            Toast.Error("切换电源计划失败:" + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestartExplorerAsync()
    {
        if (!await Ui.ConfirmAsync("重启资源管理器", "确定重启资源管理器吗?",
            "桌面和任务栏会短暂消失再恢复,已打开的文件夹窗口会关闭。")) return;
        try
        {
            await Task.Run(TweakService.RestartExplorer).ConfigureAwait(true);
            Toast.Success("资源管理器已重启");
        }
        catch (Exception ex)
        {
            Log.Error("重启资源管理器失败", ex);
            Toast.Error("重启失败:" + ex.Message);
        }
    }

    private async Task AskRestartExplorerAsync()
    {
        if (await Ui.ConfirmAsync("需要重启资源管理器", "去箭头、经典菜单等外观修改需要重启资源管理器才能看到效果,现在重启吗?",
            "桌面和任务栏会短暂消失再恢复。"))
        {
            await Task.Run(TweakService.RestartExplorer).ConfigureAwait(true);
            Toast.Success("资源管理器已重启");
        }
    }
}
