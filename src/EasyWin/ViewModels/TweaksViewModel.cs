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
        Func<Task>? promptAfterApply = null)
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
        ReloadState(silent: true);
    }

    public string Title { get; }
    public string Description { get; }
    public string Symbol { get; }
    public string ApplyText { get; }
    public string RestoreText { get; }

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
        IsBusy = true;
        try
        {
            var target = !IsApplied;
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

/// <summary>系统设置页:更新控制、桌面外观、性能与安全。</summary>
public partial class TweaksViewModel : ObservableObject
{
    private readonly NetworkService _network;

    public TweaksViewModel(NetworkService network)
    {
        _network = network;

        UpdateSection =
        [
            new TweakCardViewModel("禁用 Windows 自动更新",
                "组策略 NoAutoUpdate + 禁用 wuauserv + 禁用 WaaSMedicSvc(防自动恢复)。建议定期恢复更新以获取安全补丁。",
                "ArrowClockwise24", TweakService.IsUpdateDisabled,
                async target => await Task.Run(() => TweakService.SetUpdateDisabled(target)),
                "禁用更新", "恢复更新",
                () => { }),
        ];

        AppearanceSection =
        [
            new TweakCardViewModel("去除快捷方式小箭头",
                "修改 Shell Icons 注册表项,生效需重启资源管理器。",
                "Link24", TweakService.IsShortcutArrowHidden,
                async target => { TweakService.SetShortcutArrowHidden(target); return (string?)null; },
                "去箭头", "恢复箭头",
                () => { }, promptAfterApply: AskRestartExplorerAsync),
            new TweakCardViewModel("隐藏「快捷方式」字样",
                "新建快捷方式时不再自动添加「- 快捷方式」后缀。",
                "Edit24", TweakService.IsShortcutPrefixHidden,
                async target => { TweakService.SetShortcutPrefixHidden(target); return (string?)null; },
                "隐藏字样", "恢复字样",
                () => { }),
        ];

        PerformanceSection =
        [
            new TweakCardViewModel("启用远程桌面",
                "允许其他设备通过远程桌面连接本机(家庭版不支持作为被控端)。",
                "Desktop24", TweakService.IsRemoteDesktopEnabled,
                async target => { TweakService.SetRemoteDesktop(target); return (string?)null; },
                "开启远程", "关闭远程",
                () => { }),
            new TweakCardViewModel("视觉效果设为最佳性能",
                "关闭窗口动画、阴影等视觉效果,低配机更流畅;重启资源管理器后完全生效。",
                "Flash24", TweakService.IsBestPerformance,
                async target => { TweakService.SetBestPerformance(target); return (string?)null; },
                "最佳性能", "恢复默认",
                () => { }),
            new TweakCardViewModel("开启防火墙",
                "域/专用/公用全部配置文件的防火墙。仅建议在受信任的内网环境临时关闭,用完记得开回来!",
                "Shield24", TweakService.IsFirewallEnabled,
                async target => { await TweakService.SetFirewallAsync(target); return (string?)null; },
                "开启防火墙", "关闭防火墙",
                () => { }),
        ];
    }

    public ObservableCollection<TweakCardViewModel> UpdateSection { get; }
    public ObservableCollection<TweakCardViewModel> AppearanceSection { get; }
    public ObservableCollection<TweakCardViewModel> PerformanceSection { get; }

    public ObservableCollection<PowerPlan> PowerPlans { get; } = [];

    [ObservableProperty] private PowerPlan? _activePlan;
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var plans = await TweakService.GetPowerPlansAsync().ConfigureAwait(true);
        PowerPlans.Clear();
        foreach (var plan in plans) PowerPlans.Add(plan);
        ActivePlan = plans.FirstOrDefault(p => p.IsActive);
        foreach (var card in UpdateSection.Concat(AppearanceSection).Concat(PerformanceSection))
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
        if (await Ui.ConfirmAsync("需要重启资源管理器", "去箭头等外观修改需要重启资源管理器才能看到效果,现在重启吗?",
            "桌面和任务栏会短暂消失再恢复。"))
        {
            await Task.Run(TweakService.RestartExplorer).ConfigureAwait(true);
            Toast.Success("资源管理器已重启");
        }
    }
}
