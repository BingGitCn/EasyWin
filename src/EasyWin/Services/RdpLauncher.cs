using System.Diagnostics;
using System.IO;
using System.Text;
using EasyWin.Models;

namespace EasyWin.Services;

/// <summary>一键启动远程桌面:写 .rdp 配置 + cmdkey 预置凭据 + 拉起 mstsc。</summary>
public static class RdpLauncher
{
    /// <summary>解析 "host" 或 "host:端口"。</summary>
    public static (string host, int port) ParseServer(string server)
    {
        var s = (server ?? "").Trim();
        if (s.Length == 0) return ("", 3389);
        var idx = s.LastIndexOf(':');
        if (idx > 0 && int.TryParse(s[(idx + 1)..], out var port) && port is > 0 and < 65536 && !s.Contains(']'))
            return (s[..idx].Trim('[', ']'), port);
        return (s.Trim('[', ']'), 3389);
    }

    public static async Task<(bool ok, string message)> LaunchAsync(RdpProfile profile)
    {
        var (host, port) = ParseServer(profile.Server);
        if (host.Length == 0)
            return (false, "服务器地址为空");

        // 1) 预置凭据到 Windows 凭据管理器(mstsc 自动取用,免输密码)
        if (profile.RememberPassword)
        {
            var password = profile.GetPassword();
            var user = string.IsNullOrWhiteSpace(profile.UserName) ? Environment.UserName : profile.UserName.Trim();
            if (!string.IsNullOrEmpty(password))
            {
                await CommandRunner.RunAsync("cmdkey", $"/delete:TERMSRV/{host}").ConfigureAwait(false); // 忽略不存在的情况
                var cred = await CommandRunner.RunAsync("cmdkey",
                    $"/generic:TERMSRV/{host}", $"/user:{user}", $"/pass:{password}").ConfigureAwait(false);
                if (!cred.Ok)
                    return (false, "写入凭据管理器失败:" + cred.AllText);
            }
        }

        // 2) 生成 .rdp 文件
        var rdpPath = BuildRdpFile(profile, host, port);

        // 3) 启动 mstsc
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "mstsc.exe",
                Arguments = $"\"{rdpPath}\"",
                UseShellExecute = true,
            });
            return (true, $"正在连接 {profile.Server}");
        }
        catch (Exception ex)
        {
            Log.Error("启动 mstsc 失败", ex);
            return (false, "启动远程桌面失败:" + ex.Message);
        }
    }

    private static string BuildRdpFile(RdpProfile profile, string host, int port)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EasyWin");
        Directory.CreateDirectory(dir);
        var safe = string.Join("", profile.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        if (safe.Length == 0) safe = "connect";
        var path = Path.Combine(dir, safe + ".rdp");

        var sb = new StringBuilder();
        sb.AppendLine($"screen mode id:i:{(profile.FullScreen ? 2 : 1)}");
        sb.AppendLine($"desktopwidth:i:{profile.Width}");
        sb.AppendLine($"desktopheight:i:{profile.Height}");
        sb.AppendLine("session bpp:i:32");
        sb.AppendLine($"full address:s:{host}:{port}");
        if (!string.IsNullOrWhiteSpace(profile.UserName))
            sb.AppendLine($"username:s:{profile.UserName.Trim()}");
        sb.AppendLine($"administrative session:i:{(profile.AdminSession ? 1 : 0)}");
        sb.AppendLine("compression:i:1");
        sb.AppendLine("keyboardhook:i:2");
        sb.AppendLine("audiomode:i:0");
        sb.AppendLine("disable wallpaper:i:0");
        sb.AppendLine("allow font smoothing:i:1");
        sb.AppendLine("allow desktop composition:i:1");
        sb.AppendLine("connection type:i:7");
        File.WriteAllText(path, sb.ToString(), Encoding.Unicode);
        return path;
    }
}
