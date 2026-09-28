using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWin.Services;

namespace EasyWin.ViewModels;

/// <summary>清理页的一行:一个清理目标的勾选、扫描大小与执行。</summary>
public partial class CleanerItemViewModel : ObservableObject
{
    private readonly CleanupTarget _target;
    private long _bytes;
    private int _files;

    public CleanerItemViewModel(CleanupTarget target)
    {
        _target = target;
        Title = target.Name;
        Description = target.Description;
        Symbol = target.Symbol;
    }

    public string Title { get; }
    public string Description { get; }
    public string Symbol { get; }

    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _statusText = "未扫描";
    [ObservableProperty] private bool _hasResult;

    /// <summary>扫描完成后的摘要文案,如「32.6 MB · 1,204 个文件」「12 项」「无垃圾」。</summary>
    public string SizeText
    {
        get
        {
            if (!HasResult) return "";
            if (_files == 0) return "无垃圾";
            return _bytes == 0 ? $"{_files:N0} 项" : $"{FormatBytes(_bytes)} · {_files:N0} 个文件";
        }
    }

    public async Task ScanAsync()
    {
        StatusText = "扫描中…";
        HasResult = false;
        try
        {
            var result = await Task.Run(_target.Scan).ConfigureAwait(true);
            _bytes = result.Bytes;
            _files = result.Files;
            HasResult = true;
            StatusText = result.Note ?? "";
        }
        catch (Exception ex)
        {
            Log.Error($"扫描「{Title}」失败", ex);
            StatusText = "扫描失败";
        }
        OnPropertyChanged(nameof(SizeText));
    }

    /// <summary>执行清理;目标未扫描过则跳过。返回释放的字节数(用于合计)。</summary>
    public async Task<long> CleanAsync()
    {
        if (!HasResult || _files == 0) return 0;
        StatusText = "清理中…";
        try
        {
            var result = await Task.Run(_target.Clean).ConfigureAwait(true);
            _bytes = 0;
            _files = 0;
            HasResult = true;
            StatusText = result.Note ?? "已清理";
            OnPropertyChanged(nameof(SizeText));
            return result.FreedBytes;
        }
        catch (Exception ex)
        {
            Log.Error($"清理「{Title}」失败", ex);
            StatusText = "清理失败";
            OnPropertyChanged(nameof(SizeText));
            return 0;
        }
    }

    internal long Bytes => _bytes;
    internal int Files => _files;

    internal static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):F1} KB",
        _ => $"{bytes} B",
    };
}

/// <summary>清理页:扫描全部目标 → 勾选 → 一键清理。</summary>
public partial class CleanerViewModel : ObservableObject
{
    public CleanerViewModel()
    {
        Items = new ObservableCollection<CleanerItemViewModel>(
            CleanerService.Targets.Select(t => new CleanerItemViewModel(t)));
        _ = ScanAllAsync(); // 进入页面自动扫一轮
    }

    public ObservableCollection<CleanerItemViewModel> Items { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _progressText = "";

    public bool IsNotBusy => !IsBusy;
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    private IEnumerable<CleanerItemViewModel> Selected =>
        Items.Where(i => i.IsSelected && i.HasResult && i.Files > 0);

    [RelayCommand]
    public async Task ScanAllAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            foreach (var item in Items)
            {
                ProgressText = $"正在扫描:{item.Title}…";
                await item.ScanAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            ProgressText = "";
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task CleanSelectedAsync()
    {
        if (IsBusy) return;
        var targets = Selected.ToList();
        if (targets.Count == 0)
        {
            Toast.Show("请先扫描,再勾选要清理的目标");
            return;
        }

        var totalBytes = targets.Sum(t => t.Bytes);
        var totalFiles = targets.Sum(t => t.Files);
        var summary = $"{targets.Count} 个目标,共 {CleanerItemViewModel.FormatBytes(totalBytes)} / {totalFiles:N0} 项";
        if (!await Ui.ConfirmAsync("确认清理", $"将清理{summary},删除后不可恢复。", "被占用或受保护的文件会自动跳过。")) return;

        IsBusy = true;
        try
        {
            long freed = 0;
            foreach (var item in targets)
            {
                ProgressText = $"正在清理:{item.Title}…";
                freed += await item.CleanAsync().ConfigureAwait(true);
            }
            Toast.Success($"清理完成,释放 {CleanerItemViewModel.FormatBytes(freed)}");
        }
        finally
        {
            ProgressText = "";
            IsBusy = false;
        }
    }
}
