using System.IO;

namespace EasyWin.Services;

/// <summary>开发工具:winget 包 + 本机安装检测(检测只认标准路径,便携版/自定义路径装的不算)。</summary>
public sealed record DevToolInfo(
    string Name,
    string Description,
    string Symbol,
    string WingetId,
    Func<bool> Detect,
    /// <summary>传给 winget --override 的安装器参数(如 VS 工作负载),null 表示默认静默安装。</summary>
    string? OverrideArgs = null,
    /// <summary>安装超时(VS 全家桶可能要几十分钟)。</summary>
    TimeSpan? Timeout = null,
    /// <summary>大体积/长耗时工具,点安装前弹一次确认;null 不确认。</summary>
    string? ConfirmNote = null);

/// <summary>开发环境页:常用开发工具的检测与 winget 一键安装(理念参考微软 WindowsDeveloperConfig 开发机配置,MIT)。</summary>
public static class DevToolService
{
    private static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static string ProgramFilesX86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static bool ExistsInProgramFiles(string relative) =>
        File.Exists(Path.Combine(ProgramFiles, relative));

    private static bool ExistsInLocalAppData(string relative) =>
        File.Exists(Path.Combine(LocalAppData, relative));

    public static readonly IReadOnlyList<DevToolInfo> Tools =
    [
        new("Windows Terminal",
            "微软官方现代终端,多标签、窗格、GPU 加速渲染。Win11 通常已预装。",
            "CodeBlock24", "Microsoft.WindowsTerminal",
            () => ExistsInLocalAppData(@"Microsoft\WindowsApps\wt.exe")),

        new("PowerShell 7",
            "跨平台版 PowerShell(pwsh),与系统自带 5.1 并存,开发脚本主力。",
            "Code24", "Microsoft.PowerShell",
            () => ExistsInProgramFiles(@"PowerShell\7\pwsh.exe")),

        new("Git",
            "版本控制本体,winget 装的是机器级完整版(含 Git Bash)。",
            "Branch24", "Git.Git",
            () => ExistsInProgramFiles(@"Git\cmd\git.exe")),

        new("GitHub CLI",
            "gh 命令行:PR、Issue、Release 全在终端里办,配合 Copilot CLI。",
            "AnimalCat24", "GitHub.cli",
            () => ExistsInProgramFiles(@"GitHub CLI\gh.exe")),

        new("VS Code",
            "轻量编辑器,装机量最大的开发工具。",
            "Code24", "Microsoft.VisualStudioCode",
            () => ExistsInLocalAppData(@"Programs\Microsoft VS Code\Code.exe") ||
                  ExistsInProgramFiles(@"Microsoft VS Code\Code.exe")),

        new(".NET 8 SDK",
            "编译运行 .NET 项目的 SDK(EasyWin 本身就是 .NET 8 应用),含 dotnet CLI。",
            "Cube24", "Microsoft.DotNet.SDK.8",
            () =>
            {
                var dotnet = Path.Combine(ProgramFiles, "dotnet", "dotnet.exe");
                if (!File.Exists(dotnet)) return false;
                // dotnet.exe 只是运行时入口,SDK 与否要看有没有可用的 SDK 目录
                var sdks = Path.Combine(ProgramFiles, "dotnet", "sdk");
                return Directory.Exists(sdks) && Directory.GetDirectories(sdks).Length > 0;
            },
            Timeout: TimeSpan.FromMinutes(20)),

        new("Python 3",
            "官方 CPython(含 pip 与 py 启动器),数据/脚本/AI 生态入门件。",
            "AnimalTurtle24", "Python.Python.3.12",
            () => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "py.exe")) ||
                  ExistsInLocalAppData(@"Programs\Python\Launcher\py.exe"),
            Timeout: TimeSpan.FromMinutes(15)),

        new("Node.js LTS",
            "JavaScript 运行时 LTS 版,前端工具链与本地脚本依赖。",
            "Leaf24", "OpenJS.NodeJS.LTS",
            () => ExistsInProgramFiles(@"nodejs\node.exe"),
            Timeout: TimeSpan.FromMinutes(15)),

        new("PowerToys",
            "微软官方效率合集:窗口分区、取色器、批量重命名、键盘映射等。",
            "PuzzlePiece24", "Microsoft.PowerToys",
            () => ExistsInLocalAppData(@"PowerToys\PowerToys.exe") ||
                  ExistsInProgramFiles(@"PowerToys\PowerToys.exe"),
            Timeout: TimeSpan.FromMinutes(20)),

        new("Visual Studio 2026 Community",
            "全功能 IDE,默认带「.NET 桌面开发」工作负载(C++/Web 等装完在 VS Installer 里补勾)。",
            "AppGeneric24", "Microsoft.VisualStudio.2026.Community",
            () => new[] { "Community", "Professional", "Enterprise" }.Any(e =>
                File.Exists(Path.Combine(ProgramFiles, "Microsoft Visual Studio", "2026", e, "Common7", "IDE", "devenv.exe"))),
            OverrideArgs: "--passive --norestart --wait --add Microsoft.VisualStudio.Workload.ManagedDesktop --includeRecommended",
            Timeout: TimeSpan.FromMinutes(45),
            ConfirmNote: "下载约 2~4 GB,安装需要 10~40 分钟,期间会弹出 VS 安装器进度窗口,请保持网络与供电。确定现在安装吗?"),
    ];

    /// <summary>winget 完整路径:提权进程的 PATH 可能不含用户级 App 执行别名目录,优先探测完整路径。</summary>
    public static string? ResolveWinget()
    {
        var alias = Path.Combine(LocalAppData, @"Microsoft\WindowsApps\winget.exe");
        if (File.Exists(alias)) return alias;
        if (ExistsInProgramFilesX86()) return Path.Combine(ProgramFilesX86, @"Microsoft\WindowsApps\winget.exe");

        var where = CommandRunner.Run("where.exe", "winget");
        if (where.Ok && where.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(File.Exists) is { } found)
            return found;
        return null;

        bool ExistsInProgramFilesX86() => File.Exists(Path.Combine(ProgramFilesX86, @"Microsoft\WindowsApps\winget.exe"));
    }

    /// <summary>winget 静默安装;VS 通过 override 带工作负载。阻塞至完成(调用方放后台线程)。</summary>
    public static CmdResult Install(DevToolInfo tool)
    {
        var winget = ResolveWinget()
            ?? throw new InvalidOperationException("未找到 winget(应用安装程序),请先在 Microsoft Store 安装「应用安装程序」");

        var args = new List<string>
        {
            "install", "--id", tool.WingetId, "--exact", "--silent",
            "--accept-package-agreements", "--accept-source-agreements",
        };
        if (tool.OverrideArgs != null)
            args.AddRange(["--override", tool.OverrideArgs]);

        return CommandRunner.RunAsync(winget, tool.Timeout ?? TimeSpan.FromMinutes(15), [.. args])
            .GetAwaiter().GetResult();
    }

    /// <summary>0 成功;3010 成功但需重启后才完整(VS 工作负载常见)。</summary>
    public static bool IsSuccessExit(CmdResult result) => result.ExitCode is 0 or 3010;
}
