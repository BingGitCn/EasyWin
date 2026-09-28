using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWin.Services;

namespace EasyWin.ViewModels;

/// <summary>使用统计一行:排名、进程、时长、占比条。</summary>
public record UsageRow(int Rank, string Process, string DurationText, double Percent);

/// <summary>使用统计页:软件使用时长(轻量版,理念参考 Planshit/Tai)。数据仅存本地。</summary>
public partial class UsageStatsViewModel : ObservableObject
{
    public UsageStatsViewModel()
    {
        _trackingEnabled = SettingsStore.UsageTrackingEnabled;
        SelectRangeCommand = new RelayCommand<string>(SelectRange);
        SelectRange("today");
    }

    public ObservableCollection<UsageRow> Rows { get; } = [];

    [ObservableProperty] private bool _trackingEnabled;
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _rangeText = "今日";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _todayActive = true;
    [ObservableProperty] private bool _weekActive;
    [ObservableProperty] private bool _monthActive;

    private int _rangeDays = 1;

    partial void OnTrackingEnabledChanged(bool value)
    {
        UsageTrackerService.SetEnabled(value);
        Toast.Show(value ? "使用统计已开启,数据仅存本地" : "使用统计已关闭");
        if (value) SelectRange(_rangeDays == 1 ? "today" : _rangeDays == 7 ? "week" : "month");
    }

    /// <summary>范围切换(today / week / month)。</summary>
    private void SelectRange(string? range)
    {
        _rangeDays = range switch
        {
            "week" => 7,
            "month" => 30,
            _ => 1,
        };
        RangeText = _rangeDays switch
        {
            7 => "近 7 天",
            30 => "近 30 天",
            _ => "今日",
        };
        TodayActive = _rangeDays == 1;
        WeekActive = _rangeDays == 7;
        MonthActive = _rangeDays == 30;
        _ = RefreshAsync();
    }

    public RelayCommand<string> SelectRangeCommand { get; }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Rows.Clear();
        var stats = await System.Threading.Tasks.Task.Run(() => UsageTrackerService.GetStats(_rangeDays))
            .ConfigureAwait(true);

        if (stats.TotalHours <= 0)
        {
            IsEmpty = true;
            SummaryText = TrackingEnabled
                ? "刚开始统计,过一会儿再来看"
                : "统计未开启——打开上方开关后,托盘常驻期间会自动记录";
            return;
        }

        IsEmpty = false;
        SummaryText = $"共 {stats.Days} 天有记录 · 总活跃 {FormatHours(stats.TotalHours)} · 数据仅存本地";
        var top = stats.Entries.Take(10).ToList();
        var restHours = stats.Entries.Skip(10).Sum(e => e.Hours);
        var rank = 1;
        foreach (var entry in top)
        {
            Rows.Add(new UsageRow(rank++, entry.Process, FormatHours(entry.Hours), Math.Round(entry.Percent, 1)));
        }
        if (stats.Entries.Count > 10)
            Rows.Add(new UsageRow(rank, "其他", FormatHours(restHours),
                Math.Round(stats.TotalHours > 0 ? restHours / stats.TotalHours * 100 : 0, 1)));
    }

    private static string FormatHours(double hours) => hours switch
    {
        >= 1 => $"{(int)hours} 小时 {Math.Round(hours % 1 * 60)} 分",
        >= 1 / 60.0 => $"{Math.Round(hours * 60)} 分钟",
        _ => "不足 1 分钟",
    };
}
