using System.Diagnostics;
using System.Text.RegularExpressions;
using EasyWin.Models;

namespace EasyWin.Services;

/// <summary>端口占用查询:解析 netstat -ano 输出(数字表格不受本地化影响)并补充进程信息。</summary>
public static class PortLookup
{
    public static List<PortUsage> FindUsages(int port)
    {
        var result = new List<PortUsage>();
        Collect(result, "TCP", port, new Regex(@"^\s*TCP\s+(\S+)\s+(\S+)\s+(\S+)\s+(\d+)\s*$"));
        Collect(result, "UDP", port, new Regex(@"^\s*UDP\s+(\S+)\s+(\S+)\s+(\d+)\s*$"));
        return result
            .OrderBy(u => u.Protocol)
            .ThenBy(u => u.State == "LISTENING" ? 0 : 1)
            .ThenBy(u => u.ProcessName, StringComparer.CurrentCulture)
            .ToList();
    }

    private static void Collect(List<PortUsage> result, string protocol, int port, Regex lineRegex)
    {
        var args = protocol == "TCP"
            ? new[] { "-ano", "-p", "tcp" }
            : new[] { "-ano", "-p", "udp" };
        var r = CommandRunner.Run("netstat", args);
        if (!r.Ok)
            throw new InvalidOperationException($"netstat 执行失败:{r.AllText}");

        foreach (var line in r.Output.Split('\n'))
        {
            var m = lineRegex.Match(line);
            if (!m.Success) continue;

            var local = m.Groups[1].Value;
            if (!MatchesPort(local, port)) continue;

            var usage = new PortUsage
            {
                Protocol = protocol,
                Local = local,
                Remote = protocol == "TCP" ? m.Groups[2].Value : "",
                State = protocol == "TCP" ? m.Groups[3].Value : "无连接状态",
                ProcessId = int.Parse(m.Groups[4].Value),
            };
            FillProcess(usage);
            result.Add(usage);
        }
    }

    /// <summary>端点形如 0.0.0.0:8080 或 [::]:8080,取最后一个冒号后的端口比对。</summary>
    private static bool MatchesPort(string endpoint, int port)
    {
        var idx = endpoint.LastIndexOf(':');
        return idx >= 0 && int.TryParse(endpoint[(idx + 1)..], out var p) && p == port;
    }

    private static void FillProcess(PortUsage usage)
    {
        try
        {
            using var p = Process.GetProcessById(usage.ProcessId);
            usage.ProcessName = p.ProcessName;
            try { usage.ProcessPath = p.MainModule?.FileName ?? ""; }
            catch { /* 系统进程可能拒绝读取模块路径 */ }
        }
        catch
        {
            usage.ProcessName = "(进程已退出或无法访问)";
        }
    }
}
