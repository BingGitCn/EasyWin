using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using DnsClient;
using DnsClient.Protocol;

namespace EasyWin.Services;

/// <summary>单次 Ping 结果。</summary>
public record PingSample(bool Success, double LatencyMs);

/// <summary>路由跟踪的一跳。Address 为 * 表示该跳无响应。</summary>
public record TracerouteHop(int Hop, string Address, double LatencyMs, bool Reached, bool TimedOut);

/// <summary>DNS 记录查询结果。</summary>
public record DnsRecord(string Type, string Value, string Ttl);

/// <summary>本机网络状态表的一行(ARP/TCP 连接/监听端口通用)。</summary>
public record NetTableRow(string Col1, string Col2, string Col3, string Col4, string Col5);

/// <summary>
/// 网络诊断引擎:Ping 监视、路由跟踪、DNS 记录查询、端口扫描、HTTP 响应头、ARP/连接/监听表、子网计算。
/// 功能清单参考 BornToBeRoot/NETworkManager(GPL),实现全部自写,未使用其代码。
/// </summary>
public static class DiagnosticsService
{
    // ---------------- Ping ----------------

    /// <summary>单次 Ping(2 秒超时,32 字节载荷)。</summary>
    public static async Task<PingSample> PingOnceAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 2000, new byte[32]).ConfigureAwait(false);
            return reply.Status == IPStatus.Success
                ? new PingSample(true, reply.RoundtripTime)
                : new PingSample(false, 0);
        }
        catch
        {
            return new PingSample(false, 0);
        }
    }

    /// <summary>路由跟踪的一跳:TTL=n 的探测包,TimeExceeded 返回中间路由,Success 即到达。</summary>
    public static async Task<TracerouteHop> TraceHopAsync(string host, int ttl)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 1500, new byte[32], new PingOptions(ttl, dontFragment: true))
                .ConfigureAwait(false);
            return reply.Status switch
            {
                IPStatus.Success => new TracerouteHop(ttl,
                    reply.Address.ToString(), reply.RoundtripTime, Reached: true, TimedOut: false),
                IPStatus.TimedOut => new TracerouteHop(ttl, "*", 0, Reached: false, TimedOut: true),
                _ => new TracerouteHop(ttl, reply.Address.ToString(), reply.RoundtripTime, Reached: false, TimedOut: false),
            };
        }
        catch
        {
            return new TracerouteHop(ttl, "*", 0, Reached: false, TimedOut: true);
        }
    }

    // ---------------- DNS 查询(DnsClient,MIT) ----------------

    public static readonly string[] DnsTypes = ["全部", "A", "AAAA", "CNAME", "MX", "TXT", "NS", "SOA", "PTR"];

    /// <summary>查询域名记录;type=全部 时逐类查询并汇总。查询失败抛出异常由调用方提示。</summary>
    public static async Task<List<DnsRecord>> DnsLookupAsync(string domain, string type)
    {
        var client = new LookupClient(new LookupClientOptions { Timeout = TimeSpan.FromSeconds(3) });
        var types = type == "全部"
            ? new[] { "A", "AAAA", "CNAME", "MX", "TXT", "NS" }
            : [type];

        var records = new List<DnsRecord>();
        foreach (var t in types)
        {
            IDnsQueryResponse response;
            try
            {
                response = await client.QueryAsync(domain, ParseType(t)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{t} 记录查询失败:{ex.Message}");
            }

            foreach (var record in response.Answers)
            {
                var value = record switch
                {
                    ARecord a => a.Address.ToString(),
                    AaaaRecord aaaa => aaaa.Address.ToString(),
                    CNameRecord cname => cname.CanonicalName,
                    MxRecord mx => $"{mx.Preference} {mx.Exchange}",
                    NsRecord ns => ns.NSDName,
                    PtrRecord ptr => ptr.PtrDomainName,
                    SoaRecord soa => $"{soa.MName} (管理员 {soa.RName}, 序号 {soa.Serial})",
                    TxtRecord txt => string.Concat(txt.Text),
                    _ => record.ToString(),
                };
                records.Add(new DnsRecord(t, value, $"{record.TimeToLive}s"));
            }
        }

        if (records.Count == 0)
            throw new InvalidOperationException("没有查询到记录(域名不存在或该类型无记录)");
        return records;
    }

    private static QueryType ParseType(string t) => t switch
    {
        "A" => QueryType.A,
        "AAAA" => QueryType.AAAA,
        "CNAME" => QueryType.CNAME,
        "MX" => QueryType.MX,
        "TXT" => QueryType.TXT,
        "NS" => QueryType.NS,
        "SOA" => QueryType.SOA,
        "PTR" => QueryType.PTR,
        _ => QueryType.A,
    };

    // ---------------- 端口扫描 ----------------

    /// <summary>并发扫描远程主机端口(复用 LanScanner 的 TCP 连通探测),只保留开放的端口。</summary>
    public static async Task<List<int>> ScanPortsAsync(string host, int[] ports, int timeoutMs, IProgress<int>? progress)
    {
        var open = new List<int>();
        var done = 0;
        using var gate = new SemaphoreSlim(64);
        var tasks = ports.Select(async port =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (await LanScanner.IsPortOpenAsync(host, port, timeoutMs).ConfigureAwait(false))
                    lock (open) { open.Add(port); }
            }
            finally
            {
                Interlocked.Increment(ref done);
                progress?.Report(done);
                gate.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return open.OrderBy(p => p).ToList();
    }

    /// <summary>解析端口描述文本:逗号/空格分隔 + "起-止" 区间(如 "22,80,443,1000-2000")。</summary>
    public static int[] ParsePorts(string text)
    {
        var ports = new List<int>();
        foreach (var part in text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.Split('-');
            if (range.Length == 2 && int.TryParse(range[0], out var lo) && int.TryParse(range[1], out var hi))
            {
                for (var p = Math.Max(1, lo); p <= Math.Min(65535, hi); p++) ports.Add(p);
            }
            else if (int.TryParse(part, out var port) && port is >= 1 and <= 65535)
            {
                ports.Add(port);
            }
        }
        return ports.Distinct().OrderBy(p => p).ToArray();
    }

    // ---------------- HTTP 响应头 ----------------

    public static async Task<List<(string Name, string Value)>> GetHttpHeadersAsync(string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;

        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("EasyWin/1.6");

        using var response = await http.SendAsync(request).ConfigureAwait(false);

        var headers = new List<(string, string)> { ("状态码", $"{(int)response.StatusCode} {response.StatusCode}") };
        foreach (var header in response.Headers)
            headers.Add((header.Key, string.Join(", ", header.Value)));
        foreach (var header in response.Content.Headers)
            headers.Add((header.Key, string.Join(", ", header.Value)));
        return headers;
    }

    // ---------------- 本机网络状态(ARP / TCP 连接 / 监听端口) ----------------

    /// <summary>ARP 表:解析 arp -a 数据行(表头随系统语言变化,数据行格式固定)。</summary>
    public static async Task<List<NetTableRow>> GetArpEntriesAsync()
    {
        var result = await CommandRunner.RunAsync("arp", "-a").ConfigureAwait(false);
        var rows = new List<NetTableRow>();
        foreach (Match m in Regex.Matches(result.Output,
                     @"^\s*(\d{1,3}(?:\.\d{1,3}){3})\s+([0-9a-fA-F]{2}(?:-[0-9a-fA-F]{2}){5})\s+(\S+)\s*$",
                     RegexOptions.Multiline))
        {
            rows.Add(new NetTableRow(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, "", ""));
        }
        return rows;
    }

    /// <summary>TCP 连接与监听端口:解析 netstat -ano,并把 PID 映射为进程名。</summary>
    public static async Task<(List<NetTableRow> Connections, List<NetTableRow> Listeners)> GetTcpTablesAsync()
    {
        var result = await CommandRunner.RunAsync("netstat", "-ano").ConfigureAwait(false);
        var processNames = new Dictionary<int, string>();
        var connections = new List<NetTableRow>();
        var listeners = new List<NetTableRow>();

        foreach (Match m in Regex.Matches(result.Output,
                     @"^\s*(TCP|UDP)\s+(\S+)\s+(\S+)\s+(\S+)(?:\s+(\S+))?\s*$",
                     RegexOptions.Multiline))
        {
            var proto = m.Groups[1].Value;
            var local = m.Groups[2].Value;
            var remote = m.Groups[3].Value;
            var state = proto == "TCP" ? m.Groups[4].Value : "";
            var pidText = proto == "TCP" ? m.Groups[5].Value : m.Groups[4].Value;
            if (!int.TryParse(pidText, out var pid)) continue;

            if (!processNames.TryGetValue(pid, out var name))
            {
                name = GetProcessName(pid);
                processNames[pid] = name;
            }

            var row = new NetTableRow(proto, local, remote, state, $"{pid} {name}");
            if (proto == "UDP" || state == "LISTENING")
                listeners.Add(row);
            else
                connections.Add(row);
        }

        listeners.Sort((a, b) => string.CompareOrdinal(a.Col2, b.Col2));
        connections.Sort((a, b) => string.CompareOrdinal(a.Col3, b.Col3));
        return (connections, listeners);
    }

    private static string GetProcessName(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    // ---------------- 子网计算器 ----------------

    /// <summary>IP + 掩码(点分十进制或 /CIDR)→ 网段信息;解析失败返回 error。</summary>
    public static (string? Error, List<(string Key, string Value)> Results) CalculateSubnet(string ipText, string maskText)
    {
        if (!IPAddress.TryParse(ipText.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            return ("IP 地址格式不正确(需要 IPv4)", []);

        maskText = maskText.Trim().TrimStart('/');
        uint mask;
        if (int.TryParse(maskText, out var cidr))
        {
            if (cidr is < 0 or > 32) return ("CIDR 前缀需在 0-32 之间", []);
            mask = cidr == 0 ? 0 : uint.MaxValue << (32 - cidr);
        }
        else if (IPAddress.TryParse(maskText, out var maskIp))
        {
            mask = ToUint(maskIp);
        }
        else
        {
            return ("掩码格式不正确(如 255.255.255.0 或 /24)", []);
        }

        var ipUint = ToUint(ip);
        var network = ipUint & mask;
        var broadcast = network | ~mask;
        var total = ~mask + 1;
        var usable = total > 2 ? total - 2 : 0; // /31、/32 特殊处理
        var first = total > 2 ? network + 1 : network;
        var last = total > 2 ? broadcast - 1 : broadcast;

        var results = new List<(string, string)>
        {
            ("网络地址", ToIp(network)),
            ("广播地址", ToIp(broadcast)),
            ("子网掩码", ToIp(mask)),
            ("通配符掩码", ToIp(~mask)),
            ("CIDR 前缀", $"/{Convert.ToString(mask, 2).Count(c => c == '1')}"),
            ("可用主机数", $"{usable:N0}"),
            ("可用范围", usable > 0 ? $"{ToIp(first)} ~ {ToIp(last)}" : "—"),
            ("IP 分类", Classify(ipUint, mask)),
            ("二进制 IP", Convert.ToString(ipUint, 2).PadLeft(32, '0')),
        };
        return (null, results);
    }

    private static uint ToUint(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes);
    }

    private static string ToIp(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        Array.Reverse(bytes);
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";
    }

    private static string Classify(uint ip, uint mask)
    {
        var first = ip >> 24;
        if (first == 127) return "环回地址";
        if (first == 10) return "私网 A 类";
        if (first == 172 && (ip >> 16 & 0xFF) >= 16 && (ip >> 16 & 0xFF) <= 31) return "私网 B 类";
        if (first == 192 && (ip >> 16 & 0xFF) == 168) return "私网 C 类";
        if (first == 169 && (ip >> 16 & 0xFF) == 254) return "APIPA 自动私有地址(未获取到 DHCP)";
        if (first >= 224) return "组播/保留地址";
        return "公网地址";
    }
}
