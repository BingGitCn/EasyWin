using System.Net.NetworkInformation;
using EasyWin.Models;

namespace EasyWin.Services;

/// <summary>
/// 场景自动化:网络状态变化(防抖)后检查当前 SSID,
/// 命中「连接到某 Wi-Fi → 应用某方案」的启用规则时自动切换。
/// 应用常驻托盘,规则才能在后台持续生效。
/// </summary>
public class AutomationService
{
    private readonly NetworkService _network;
    private readonly ProfileStore _store;
    private readonly AutomationStore _rules;

    private string? _lastSsid;
    private DateTime _lastTriggerAt = DateTime.MinValue;
    private int _checking;

    public AutomationService(NetworkService network, ProfileStore store, AutomationStore rules)
    {
        _network = network;
        _store = store;
        _rules = rules;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => _ = CheckAsync();

    /// <summary>外部(如刚改完规则)可主动触发一次检查。</summary>
    public async Task CheckNowAsync()
    {
        _lastSsid = null; // 强制按"SSID 变化"路径重新评估
        await CheckAsync().ConfigureAwait(false);
    }

    private async Task CheckAsync()
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        try
        {
            await Task.Delay(2500).ConfigureAwait(false); // 防抖:等连接/DHCP 稳定

            var ssid = await Task.Run(() => WlanService.GetCurrentSsid()).ConfigureAwait(false);
            if (ssid == null) { _lastSsid = null; return; }
            if (string.Equals(ssid, _lastSsid, StringComparison.OrdinalIgnoreCase)) return;

            var previous = _lastSsid;
            _lastSsid = ssid; // 先记下,避免应用方案引起的网络抖动造成循环触发
            if (previous == null) return; // 首次事件只记录基线,不在启动时乱切网络
            if ((DateTime.Now - _lastTriggerAt).TotalSeconds < 15) return;

            var rule = _rules.Load().FirstOrDefault(r => r.Enabled
                && r.Ssid.Equals(ssid, StringComparison.OrdinalIgnoreCase));
            if (rule == null) return;

            var profile = _store.Load().FirstOrDefault(p => p.Id == rule.ProfileId);
            if (profile == null)
            {
                Toast.Warning($"自动化规则:未找到 SSID「{ssid}」要应用的方案,请到「IP 方案」页检查。");
                return;
            }

            _lastTriggerAt = DateTime.Now;
            Log.Info($"自动化规则命中: {ssid} → {profile.Name}");
            var (ok, message) = await Task.Run(() => _network.ApplyProfileAsync(profile)).ConfigureAwait(false);
            if (ok) Toast.Success($"自动化:{message}");
            else Toast.Error($"自动化切换失败:{message}");
        }
        catch (Exception ex)
        {
            Log.Warn("自动化规则检查失败: " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }
}
