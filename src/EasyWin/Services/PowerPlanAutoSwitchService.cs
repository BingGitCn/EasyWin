using System.Threading;
using System.Windows;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace EasyWin.Services;

/// <summary>
/// 按供电状态自动切换电源计划:接通电源用「插电计划」,用电池用「电池计划」。
/// 监听系统电源模式变化(插拔电源、睡眠唤醒),应用常驻托盘时持续生效。
/// </summary>
public class PowerPlanAutoSwitchService
{
    private int _checking;

    public PowerPlanAutoSwitchService()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        // 启动后同步一次当前供电状态对应的计划(错开启动高峰)
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(6000).ConfigureAwait(false);
            await CheckNowAsync().ConfigureAwait(false);
        });
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // StatusChange = 电源来源变化(插电↔电池);Resume 后状态也可能变,一并复核
        if (e.Mode != PowerModes.StatusChange && e.Mode != PowerModes.Resume) return;
        _ = CheckNowAsync();
    }

    /// <summary>立即按当前供电状态评估并切换(改配置后也可主动触发)。</summary>
    public async System.Threading.Tasks.Task CheckNowAsync()
    {
        if (!SettingsStore.PowerAutoEnabled) return;
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        try
        {
            await System.Threading.Tasks.Task.Delay(1200).ConfigureAwait(false); // 等电源状态稳定

            var status = WinForms.SystemInformation.PowerStatus.PowerLineStatus;
            if (status == WinForms.PowerLineStatus.Unknown) return;
            var onBattery = status == WinForms.PowerLineStatus.Offline; // Offline = 使用电池

            var target = onBattery ? SettingsStore.BatteryPlanGuid : SettingsStore.AcPlanGuid;
            if (string.IsNullOrEmpty(target)) return;

            var active = await TweakService.GetActivePlanGuidAsync().ConfigureAwait(false);
            if (active == null || string.Equals(active, target, StringComparison.OrdinalIgnoreCase)) return;

            await TweakService.SetPowerPlanAsync(target).ConfigureAwait(false);

            // 取计划名做提示
            var plans = await TweakService.GetPowerPlansAsync().ConfigureAwait(false);
            var name = plans.FirstOrDefault(p => string.Equals(p.Guid, target, StringComparison.OrdinalIgnoreCase))?.Name ?? target;
            Toast.Success($"电源已{(onBattery ? "切换为电池" : "接通")},自动启用「{name}」");
            Log.Info($"电源自动切换: {(onBattery ? "电池" : "插电")} → {name}");
        }
        catch (Exception ex)
        {
            Log.Warn("电源计划自动切换失败: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }
}
