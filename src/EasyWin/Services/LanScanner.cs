using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using EasyWin.Models;

namespace EasyWin.Services;

public record LanHost(string Ip, int Port, string? Hostname);

/// <summary>局域网 RDP 主机扫描:对本网段做并发 TCP 端口探测。</summary>
public static class LanScanner
{
    /// <summary>扫描各在线网卡所在 /24 网段上的 RDP 端口。仅取 /24,更大网段会裁剪避免上万次探测。</summary>
    public static async Task<List<LanHost>> ScanRdpAsync(
        IEnumerable<(string ip, string mask)> adapters, int port = 3389, CancellationToken ct = default)
    {
        var targets = BuildTargetIps(adapters);
        var results = new ConcurrentBag<LanHost>();
        using var throttle = new SemaphoreSlim(128);

        var tasks = targets.Select(async ip =>
        {
            await throttle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (await IsPortOpenAsync(ip, port, 600).ConfigureAwait(false))
                {
                    var name = await TryResolveNameAsync(ip).ConfigureAwait(false);
                    results.Add(new LanHost(ip, port, name));
                }
            }
            catch (OperationCanceledException) { }
            catch { /* 单个目标失败忽略 */ }
            finally
            {
                throttle.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        return results
            .OrderBy(h => IPAddress.Parse(h.Ip).GetAddressBytes(), Comparer<byte[]>.Create((a, b) =>
            {
                for (var i = 0; i < 4; i++)
                    if (a![i] != b![i]) return a[i].CompareTo(b[i]);
                return 0;
            }))
            .ToList();
    }

    /// <summary>由网卡 IP + 掩码推导要探测的地址列表(每网段最多 254 个)。</summary>
    private static List<string> BuildTargetIps(IEnumerable<(string ip, string mask)> adapters)
    {
        var set = new HashSet<string>();
        foreach (var (ipStr, maskStr) in adapters)
        {
            if (!NetworkService.IsValidIPv4(ipStr) || !NetworkService.IsValidIPv4(maskStr)) continue;
            var ip = IPAddress.Parse(ipStr.Trim()).GetAddressBytes();
            var mask = IPAddress.Parse(maskStr.Trim()).GetAddressBytes();
            if (ip.Length != 4 || mask.Length != 4) continue;

            var network = new byte[4];
            for (var i = 0; i < 4; i++) network[i] = (byte)(ip[i] & mask[i]);

            var prefix = NetworkPrefix.PrefixFromMask(maskStr);
            if (prefix < 24)
            {
                // 大网段裁剪到自身所在 /24,避免上万次探测
                network[2] = ip[2];
                network[3] = 0;
            }

            for (var last = 1; last <= 254; last++)
                set.Add($"{network[0]}.{network[1]}.{network[2]}.{last}");
        }
        return set.ToList();
    }

    /// <summary>探测 TCP 端口是否开放(唤醒等待、连通性检查用)。</summary>
    public static async Task<bool> IsPortOpenAsync(string ip, int port, int timeoutMs)
    {
        using var client = new TcpClient();
        try
        {
            var connectTask = client.ConnectAsync(ip, port);
            var finished = await Task.WhenAny(connectTask, Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (finished != connectTask)
            {
                // 超时放弃后连接还会在后台完成/失败,挂上观察避免未观察异常
                _ = connectTask.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                return false;
            }
            try
            {
                await connectTask.ConfigureAwait(false);
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>反向解析主机名(最佳努力,150ms 超时)。</summary>
    private static async Task<string?> TryResolveNameAsync(string ip)
    {
        try
        {
            var lookup = Dns.GetHostEntryAsync(ip);
            var finished = await Task.WhenAny(lookup, Task.Delay(150)).ConfigureAwait(false);
            if (finished != lookup) return null;
            var hostName = lookup.Result.HostName;
            // 长得像 "x.x.x.x.in-addr.arpa" 的视为未解析
            return hostName.Contains("in-addr.arpa", StringComparison.OrdinalIgnoreCase) ? null : hostName;
        }
        catch
        {
            return null;
        }
    }
}
