using System.Diagnostics;
using System.IO;
using System.Text;

namespace EasyWin.Services;

public record CmdResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
    /// <summary>合并输出(netsh 的报错信息常在标准输出里)。</summary>
    public string AllText => string.IsNullOrWhiteSpace(Error) ? Output.Trim() : $"{Output.Trim()}\n{Error.Trim()}";
}

/// <summary>进程命令执行封装:隐藏窗口、GBK 解码(netsh 在中文系统的输出)、带超时。</summary>
public static class CommandRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public static CmdResult Run(string fileName, params string[] args) =>
        RunAsync(fileName, DefaultTimeout, args).GetAwaiter().GetResult();

    public static Task<CmdResult> RunAsync(string fileName, params string[] args) =>
        RunAsync(fileName, DefaultTimeout, args);

    public static async Task<CmdResult> RunAsync(string fileName, TimeSpan timeout, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var process = new Process { StartInfo = psi };
            process.Start();

            // 不预设编码:系统可能输出 UTF-8(开启 UTF-8 全局支持)或 GBK,先读原始字节再探测
            using var msOut = new MemoryStream();
            using var msErr = new MemoryStream();
            var outTask = process.StandardOutput.BaseStream.CopyToAsync(msOut);
            var errTask = process.StandardError.BaseStream.CopyToAsync(msErr);
            var exitTask = process.WaitForExitAsync();

            var finished = await Task.WhenAny(exitTask, Task.Delay(timeout)).ConfigureAwait(false);
            if (finished != exitTask)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* 进程可能已退出 */ }
                // 等输出管道收尾(可能因流已释放而失败),避免遗留未观察异常
                try { await Task.WhenAll(outTask, errTask).ConfigureAwait(false); } catch { /* 忽略 */ }
                return new CmdResult(-1, string.Empty, "命令执行超时");
            }

            await outTask.ConfigureAwait(false);
            await errTask.ConfigureAwait(false);

            return new CmdResult(process.ExitCode, Decode(msOut.ToArray()), Decode(msErr.ToArray()));
        }
        catch (Exception ex)
        {
            Log.Error($"执行命令失败: {fileName} {string.Join(' ', args)}", ex);
            return new CmdResult(-1, string.Empty, ex.Message);
        }
    }

    /// <summary>严格按 UTF-8 解码,失败(常见于老系统 GBK 输出)时回退 GBK。</summary>
    private static string Decode(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Gbk.GetString(bytes);
        }
    }

    private static Encoding? _gbk;

    private static Encoding Gbk => _gbk ??= Encoding.GetEncoding(936);
}
