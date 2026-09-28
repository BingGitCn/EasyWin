using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWin.Services;

namespace EasyWin.ViewModels;

/// <summary>HTTP 响应头一行(XAML 绑定用 record,元组在运行时没有命名属性)。</summary>
public record HeaderRow(string Name, string Value);

/// <summary>子网计算结果一行。</summary>
public record SubnetRow(string Key, string Value);

/// <summary>网络诊断页:Ping 监视、路由跟踪、DNS 查询、端口扫描、HTTP 头、本机网络状态、子网计算。</summary>
public partial class DiagnosticsViewModel : ObservableObject
{
    public DiagnosticsViewModel()
    {
        // 初始填充常用端口与示例值,减少空页面感
        _scanPortsText = "21, 22, 23, 25, 53, 80, 110, 143, 443, 445, 3306, 3389, 5432, 8080, 8443";
        _dnsType = "A";
    }

    // ================= Ping 监视 =================

    [ObservableProperty] private string _pingHost = "223.5.5.5";
    [ObservableProperty] private bool _isPinging;
    [ObservableProperty] private string _pingStatsText = "";
    [ObservableProperty] private double[] _pingValues = [];

    private List<double> _latencies = [];
    private System.Threading.CancellationTokenSource? _pingCts;

    public bool IsNotPinging => !IsPinging;
    partial void OnIsPingingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotPinging));
        OnPropertyChanged(nameof(PingButtonText));
    }

    /// <summary>Ping 按钮文案:未运行显示「开始」,运行中显示「停止」。</summary>
    public string PingButtonText => IsPinging ? "停止" : "开始";

    [RelayCommand]
    private void TogglePing()
    {
        if (IsPinging)
        {
            _pingCts?.Cancel();
            return;
        }

        // 命令保持同步立即返回:异步 RelayCommand 执行期间会禁用按钮(CanExecute=false),
        // 「停止」就永远点不到——循环放后台任务跑,按钮始终可点
        var cts = new System.Threading.CancellationTokenSource();
        _pingCts = cts;
        IsPinging = true;
        _ = PingLoopAsync(cts.Token);
    }

    private async Task PingLoopAsync(System.Threading.CancellationToken ct)
    {
        _latencies = [];
        var sent = 0;
        var lost = 0;
        double min = double.MaxValue, max = 0, sum = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var sample = await DiagnosticsService.PingOnceAsync(PingHost.Trim()).ConfigureAwait(true);
                sent++;
                if (sample.Success)
                {
                    _latencies.Add(sample.LatencyMs);
                    min = Math.Min(min, sample.LatencyMs);
                    max = Math.Max(max, sample.LatencyMs);
                    sum += sample.LatencyMs;
                }
                else
                {
                    lost++;
                    _latencies.Add(0);
                }
                if (_latencies.Count > 60) _latencies.RemoveAt(0);
                PingValues = _latencies.ToArray();

                var avg = sent - lost > 0 ? sum / (sent - lost) : 0;
                PingStatsText = $"已发 {sent} · 丢失 {lost}({(sent == 0 ? 0 : lost * 100 / sent)}%)"
                                + (sent - lost > 0 ? $" · 平均 {avg:F0}ms / 最小 {min:F0}ms / 最大 {max:F0}ms" : "");
                try { await Task.Delay(1000, ct).ConfigureAwait(true); }
                catch (TaskCanceledException) { break; }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Ping 监视异常", ex);
        }
        finally
        {
            IsPinging = false;
            PingStatsText += "  ·  已停止";
        }
    }

    // ================= 路由跟踪 =================

    [ObservableProperty] private string _traceHost = "";
    [ObservableProperty] private bool _isTracing;
    [ObservableProperty] private string _traceProgressText = "";
    public ObservableCollection<TracerouteHop> TraceHops { get; } = [];

    public bool IsNotTracing => !IsTracing;
    partial void OnIsTracingChanged(bool value) => OnPropertyChanged(nameof(IsNotTracing));

    [RelayCommand]
    private async Task RunTraceAsync()
    {
        var host = TraceHost.Trim();
        if (host.Length == 0 || IsTracing) return;
        IsTracing = true;
        TraceHops.Clear();
        try
        {
            for (var ttl = 1; ttl <= 30; ttl++)
            {
                TraceProgressText = $"正在探测第 {ttl} 跳…";
                var hop = await DiagnosticsService.TraceHopAsync(host, ttl).ConfigureAwait(true);
                TraceHops.Add(hop);
                if (hop.Reached) break;
            }
            TraceProgressText = TraceHops.Any(h => h.Reached)
                ? $"完成,共 {TraceHops.Count} 跳"
                : "全部超时——目标不可达或禁 ICMP 响应";
        }
        catch (Exception ex)
        {
            Log.Error("路由跟踪失败", ex);
            TraceProgressText = "失败:" + ex.Message;
        }
        finally
        {
            IsTracing = false;
        }
    }

    // ================= DNS 查询 =================

    [ObservableProperty] private string _dnsDomain = "";
    [ObservableProperty] private string _dnsType;
    [ObservableProperty] private bool _isDnsBusy;
    [ObservableProperty] private string _dnsMessage = "";
    public ObservableCollection<string> DnsTypes { get; } = new(DiagnosticsService.DnsTypes);
    public ObservableCollection<DnsRecord> DnsRecords { get; } = [];

    public bool IsNotDnsBusy => !IsDnsBusy;
    partial void OnIsDnsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotDnsBusy));

    [RelayCommand]
    private async Task RunDnsAsync()
    {
        var domain = DnsDomain.Trim();
        if (domain.Length == 0 || IsDnsBusy) return;
        IsDnsBusy = true;
        DnsRecords.Clear();
        DnsMessage = "查询中…";
        try
        {
            var records = await DiagnosticsService.DnsLookupAsync(domain, DnsType).ConfigureAwait(true);
            foreach (var record in records) DnsRecords.Add(record);
            DnsMessage = $"共 {records.Count} 条记录";
        }
        catch (Exception ex)
        {
            DnsMessage = ex.Message;
        }
        finally
        {
            IsDnsBusy = false;
        }
    }

    // ================= 端口扫描 =================

    [ObservableProperty] private string _scanHost = "";
    [ObservableProperty] private string _scanPortsText;
    [ObservableProperty] private bool _isScanBusy;
    [ObservableProperty] private string _scanProgressText = "";
    [ObservableProperty] private string _scanResultText = "";

    public bool IsNotScanBusy => !IsScanBusy;
    partial void OnIsScanBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotScanBusy));

    [RelayCommand]
    private async Task RunPortScanAsync()
    {
        var host = ScanHost.Trim();
        var ports = DiagnosticsService.ParsePorts(ScanPortsText);
        if (host.Length == 0 || ports.Length == 0 || IsScanBusy) return;
        if (ports.Length > 4096)
        {
            Toast.Warning("一次最多扫描 4096 个端口,请缩小范围");
            return;
        }

        IsScanBusy = true;
        ScanResultText = "";
        try
        {
            var progress = new Progress<int>(done => ScanProgressText = $"正在扫描 {done}/{ports.Length}…");
            ScanProgressText = $"正在扫描 0/{ports.Length}…";
            var open = await DiagnosticsService.ScanPortsAsync(host, ports, 600, progress).ConfigureAwait(true);
            ScanProgressText = $"完成,共 {ports.Length} 个端口";
            ScanResultText = open.Count == 0
                ? "没有发现开放端口(主机不在线、端口被过滤或防火墙拦截)"
                : $"开放 {open.Count} 个:{string.Join(", ", open)}";
            Toast.Success(open.Count == 0 ? "端口扫描完成,无开放端口" : $"端口扫描完成,发现 {open.Count} 个开放端口");
        }
        catch (Exception ex)
        {
            Log.Error("端口扫描失败", ex);
            ScanProgressText = "失败:" + ex.Message;
        }
        finally
        {
            IsScanBusy = false;
        }
    }

    // ================= HTTP 响应头 =================

    [ObservableProperty] private string _httpUrl = "";
    [ObservableProperty] private bool _isHttpBusy;
    [ObservableProperty] private string _httpMessage = "";
    public ObservableCollection<HeaderRow> HttpHeaders { get; } = [];

    public bool IsNotHttpBusy => !IsHttpBusy;
    partial void OnIsHttpBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotHttpBusy));

    [RelayCommand]
    private async Task RunHttpAsync()
    {
        var url = HttpUrl.Trim();
        if (url.Length == 0 || IsHttpBusy) return;
        IsHttpBusy = true;
        HttpHeaders.Clear();
        HttpMessage = "请求中…";
        try
        {
            var headers = await DiagnosticsService.GetHttpHeadersAsync(url).ConfigureAwait(true);
            foreach (var (name, value) in headers) HttpHeaders.Add(new HeaderRow(name, value));
            HttpMessage = $"共 {headers.Count} 个响应头";
        }
        catch (Exception ex)
        {
            HttpMessage = "请求失败:" + ex.Message;
        }
        finally
        {
            IsHttpBusy = false;
        }
    }

    // ================= 本机网络状态 =================

    [ObservableProperty] private bool _isNetStateBusy;
    [ObservableProperty] private string _netStateMessage = "";
    public ObservableCollection<NetTableRow> ArpEntries { get; } = [];
    public ObservableCollection<NetTableRow> TcpConnections { get; } = [];
    public ObservableCollection<NetTableRow> ListeningPorts { get; } = [];

    [RelayCommand]
    private async Task RefreshNetStateAsync()
    {
        if (IsNetStateBusy) return;
        IsNetStateBusy = true;
        NetStateMessage = "读取中…";
        try
        {
            var arp = await DiagnosticsService.GetArpEntriesAsync().ConfigureAwait(true);
            var (connections, listeners) = await DiagnosticsService.GetTcpTablesAsync().ConfigureAwait(true);

            SetRows(ArpEntries, arp);
            SetRows(TcpConnections, connections);
            SetRows(ListeningPorts, listeners);
            NetStateMessage = $"ARP {arp.Count} 条 · TCP 连接 {connections.Count} 条 · 监听 {listeners.Count} 个";
        }
        catch (Exception ex)
        {
            Log.Error("读取本机网络状态失败", ex);
            NetStateMessage = "失败:" + ex.Message;
        }
        finally
        {
            IsNetStateBusy = false;
        }
    }

    private static void SetRows(ObservableCollection<NetTableRow> target, IEnumerable<NetTableRow> rows)
    {
        target.Clear();
        foreach (var row in rows) target.Add(row);
    }

    // ================= 子网计算器 =================

    [ObservableProperty] private string _subnetIp = "";
    [ObservableProperty] private string _subnetMask = "";
    [ObservableProperty] private string _subnetError = "";
    public ObservableCollection<SubnetRow> SubnetResults { get; } = [];

    [RelayCommand]
    private void CalculateSubnet()
    {
        SubnetError = "";
        SubnetResults.Clear();
        var (error, results) = DiagnosticsService.CalculateSubnet(SubnetIp, SubnetMask);
        if (error != null)
        {
            SubnetError = error;
            return;
        }
        foreach (var (key, value) in results) SubnetResults.Add(new SubnetRow(key, value));
    }
}
