using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWin.Services;

namespace EasyWin.ViewModels;

/// <summary>开发环境页的一张工具卡:检测已装与否,一键 winget 静默安装。</summary>
public partial class DevToolCardViewModel : ObservableObject
{
    public DevToolCardViewModel(DevToolInfo tool) => Tool = tool;

    public DevToolInfo Tool { get; }
    public string Title => Tool.Name;
    public string Description => Tool.Description;
    public string Symbol => Tool.Symbol;

    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private string _statusText = "检测中…";
    [ObservableProperty] private bool _isBusy;

    public bool CanInstall => !IsInstalled && !IsBusy;
    public string ActionText => IsBusy ? "安装中…" : IsInstalled ? "已安装" : "安装";

    partial void OnIsInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(ActionText));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(ActionText));
    }

    public async Task CheckAsync()
    {
        try
        {
            // 检测放线程池:个别工具要起进程/枚举目录,十张卡并行跑,进页不卡
            IsInstalled = await Task.Run(Tool.Detect).ConfigureAwait(true);
            StatusText = IsInstalled ? "已安装" : "未安装";
        }
        catch (Exception ex)
        {
            Log.Error($"检测「{Tool.Name}」安装状态失败", ex);
            StatusText = "检测失败";
        }
    }

    [RelayCommand]
    public async Task InstallAsync()
    {
        if (IsBusy || IsInstalled) return;
        if (Tool.ConfirmNote != null && !await Ui.ConfirmAsync($"安装 {Tool.Name}",
                "这个工具体积大、耗时长。", Tool.ConfirmNote).ConfigureAwait(true)) return;

        IsBusy = true;
        StatusText = "正在安装…";
        try
        {
            var result = await Task.Run(() => DevToolService.Install(Tool)).ConfigureAwait(true);
            await CheckAsync().ConfigureAwait(true);
            if (IsInstalled && DevToolService.IsSuccessExit(result))
            {
                StatusText = "已安装";
                Toast.Success(result.ExitCode == 3010
                    ? $"「{Tool.Name}」安装完成,重启系统后完全生效"
                    : $"「{Tool.Name}」安装完成");
            }
            else
            {
                StatusText = "安装未完成";
                Toast.Warning($"「{Tool.Name}」安装未完成:{Tail(result.AllText)}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"安装「{Tool.Name}」失败", ex);
            StatusText = "安装失败";
            Toast.Error($"「{Tool.Name}」安装失败:{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>winget 报错可能很长,只取最后一段(根因通常在结尾)。</summary>
    private static string Tail(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "winget 无输出";
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" | ", lines.TakeLast(3));
    }
}

/// <summary>开发环境页:开发工具 winget 安装 + Edge 浏览器策略。</summary>
public partial class DevEnvViewModel : ObservableObject
{
    public DevEnvViewModel()
    {
        ToolsSection = new ObservableCollection<DevToolCardViewModel>(
            DevToolService.Tools.Select(t => new DevToolCardViewModel(t)));

        List<TweakCardViewModel> edge =
        [
            new("跳过 Edge 首次运行体验",
                "Edge 安装/重置后不再弹欢迎页、数据导入与默认浏览器引导(HideFirstRunExperience=1,微软开发机配置同款)。",
                "Globe24", TweakService.IsEdgeFirstRunSkipped,
                async target => { TweakService.SetEdgeFirstRunSkipped(target); return (string?)null; },
                "跳过引导", "恢复首次体验",
                () => { }),
            new("Edge 新标签页设为空白页",
                "新标签页直接白板,不再弹资讯/搜索卡片(NewTabPageLocation=about:blank,微软开发机配置同款),Edge 全局生效。",
                "Globe24", TweakService.IsEdgeNewTabBlank,
                async target => { TweakService.SetEdgeNewTabBlank(target); return (string?)null; },
                "设为空白页", "恢复默认导航",
                () => { }),
        ];
        EdgeSection = new ObservableCollection<TweakCardViewModel>(edge);
    }

    public ObservableCollection<DevToolCardViewModel> ToolsSection { get; }
    public ObservableCollection<TweakCardViewModel> EdgeSection { get; }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        foreach (var card in EdgeSection)
            card.ReloadState();
        await Task.WhenAll(ToolsSection.Select(c => c.CheckAsync())).ConfigureAwait(true);
    }
}
