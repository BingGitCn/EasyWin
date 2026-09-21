using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWin.Models;
using EasyWin.Services;

namespace EasyWin.ViewModels;

/// <summary>快捷启动页:系统工具分类网格。</summary>
public partial class LauncherViewModel : ObservableObject
{
    public IReadOnlyList<LaunchGroup> Groups { get; } =
    [
        new LaunchGroup("系统工具", "Wrench24",
        [
            new LaunchItem("设备管理器", "硬件与驱动", "Settings24", "devmgmt.msc"),
            new LaunchItem("磁盘管理", "分区与格式化", "Storage24", "diskmgmt.msc"),
            new LaunchItem("计算机管理", "管理控制台合集", "Desktop24", "compmgmt.msc"),
            new LaunchItem("服务", "系统服务管理", "Grid24", "services.msc"),
            new LaunchItem("任务计划程序", "计划任务", "Clock24", "taskschd.msc"),
            new LaunchItem("事件查看器", "系统日志", "Book24", "eventvwr.msc"),
            new LaunchItem("系统信息", "软硬件概览", "Info24", "msinfo32"),
            new LaunchItem("DirectX 诊断", "显卡与声音诊断", "Play24", "dxdiag"),
            new LaunchItem("资源监视器", "CPU/磁盘/网络实时", "DataUsage24", "resmon"),
            new LaunchItem("性能监视器", "长期性能监控", "Flash24", "perfmon"),
            new LaunchItem("注册表编辑器", "谨慎修改", "Book24", "regedit"),
            new LaunchItem("本地组策略", "仅专业版及以上", "LockClosed24", "gpedit.msc", requiresPro: true),
            new LaunchItem("系统配置", "启动项/引导", "Settings24", "msconfig"),
            new LaunchItem("系统属性", "高级/远程/还原", "Desktop24", "sysdm.cpl"),
            new LaunchItem("环境变量", "PATH 等变量", "Edit24", "rundll32.exe", "sysdm.cpl,EditEnvironmentVariables"),
        ]),
        new LaunchGroup("网络工具", "Router24",
        [
            new LaunchItem("网络连接", "ncpa.cpl 网卡管理", "Router24", "ncpa.cpl"),
            new LaunchItem("高级安全防火墙", "入站/出站规则", "Shield24", "wf.msc"),
            new LaunchItem("网络状态页", "适配器/Wi-Fi 设置", "Globe24", "ms-settings:network"),
            new LaunchItem("代理设置", "系统代理配置", "Link24", "ms-settings:network-proxy"),
        ]),
        new LaunchGroup("Windows 设置", "Settings24",
        [
            new LaunchItem("Windows 更新", "检查/暂停更新", "ArrowSync24", "ms-settings:windowsupdate"),
            new LaunchItem("应用", "已安装应用管理", "AppGeneric24", "ms-settings:appsfeatures"),
            new LaunchItem("可选功能", "功能添加/移除", "Add24", "ms-settings:optionalfeatures"),
            new LaunchItem("存储", "空间感知与清理", "Storage24", "ms-settings:storagesense"),
            new LaunchItem("显示", "分辨率/缩放", "Desktop24", "ms-settings:display"),
            new LaunchItem("声音", "输出/输入设备", "Speaker028", "ms-settings:sound"),
            new LaunchItem("蓝牙和设备", "外设管理", "Laptop24", "ms-settings:bluetooth"),
            new LaunchItem("电源", "电源与睡眠", "Power24", "ms-settings:powersleep"),
            new LaunchItem("个性化", "壁纸/主题", "Star24", "ms-settings:personalization"),
            new LaunchItem("账户", "登录选项", "Person24", "ms-settings:yourinfo"),
            new LaunchItem("时间和语言", "时区/输入法", "Clock24", "ms-settings:dateandtime"),
            new LaunchItem("恢复", "重置/高级启动", "ArrowSync24", "ms-settings:recovery"),
            new LaunchItem("系统信息", "关于本机", "Info24", "ms-settings:about"),
            new LaunchItem("Windows 安全", "杀毒/防火墙", "ShieldCheckmark24", "ms-settings:windowsdefender"),
        ]),
        new LaunchGroup("常用小工具", "Rocket24",
        [
            new LaunchItem("任务管理器", "进程与性能", "Grid24", "taskmgr"),
            new LaunchItem("控制面板", "经典控制面板", "Settings24", "control"),
            new LaunchItem("程序和功能", "卸载程序", "Delete24", "appwiz.cpl"),
            new LaunchItem("凭据管理器", "保存的密码", "LockClosed24", "control.exe", "/name Microsoft.CredentialManager"),
            new LaunchItem("计算器", "标准计算器", "Grid24", "calc"),
            new LaunchItem("记事本", "文本编辑", "Book24", "notepad"),
            new LaunchItem("画图", "图片编辑", "Edit24", "mspaint"),
            new LaunchItem("截图工具", "区域截图", "Copy24", "snippingtool"),
            new LaunchItem("命令提示符", "CMD", "Chat24", "cmd"),
            new LaunchItem("PowerShell", "命令行", "Chat24", "powershell"),
        ]),
    ];

    [RelayCommand]
    private void Launch(LaunchItem? item)
    {
        if (item == null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.Command,
                Arguments = item.Arguments,
                UseShellExecute = true,
            });
        }
        catch (System.ComponentModel.Win32Exception ex) when (item.RequiresPro)
        {
            Toast.Warning($"「{item.Name}」需要专业版及以上系统才有(家庭版不可用)。");
            Log.Info($"启动 {item.Name} 失败(家庭版): {ex.Message}");
        }
        catch (Exception ex)
        {
            Toast.Error($"无法打开「{item.Name}」:{ex.Message}");
            Log.Error($"启动 {item.Name} 失败", ex);
        }
    }
}
