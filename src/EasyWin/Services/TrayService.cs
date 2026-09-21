using System.Windows;
using EasyWin.Models;
using EasyWin.ViewModels;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace EasyWin.Services;

/// <summary>
/// 托盘图标:双击/左键打开主窗口,右键菜单一键切 IP 方案、连 Wi-Fi、退出。
/// WPF 原生没有托盘支持,走 WinForms NotifyIcon(随 UseWindowsForms 引入),菜单用系统原生风格。
/// </summary>
public class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly NetworkService _network;
    private readonly ProfileStore _profileStore;
    private readonly WifiViewModel _wifi;

    private bool _hintShown;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayService(NetworkService network, ProfileStore profileStore, WifiViewModel wifi)
    {
        _network = network;
        _profileStore = profileStore;
        _wifi = wifi;

        _icon = new WinForms.NotifyIcon
        {
            Text = "EasyWin · 网络与系统工具",
            Visible = true,
        };
        using (var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!.Stream)
            _icon.Icon = new Drawing.Icon(stream);

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("打开 EasyWin", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("IP 方案", null, null); // 子菜单项在每次展开时动态重建
        menu.Items.Add("Wi-Fi", null, null);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke());
        menu.Opening += (_, _) => RebuildDynamicItems(menu);

        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) OpenRequested?.Invoke(); };
    }

    /// <summary>首次隐藏到托盘时提示一次,告知窗口还能从托盘找回。</summary>
    public void ShowMinimizedHint()
    {
        if (_hintShown) return;
        _hintShown = true;
        ShowBalloon("EasyWin 仍在运行", "已最小化到托盘:双击图标打开窗口,右键菜单可快捷切换网络或退出。");
    }

    private void RebuildDynamicItems(WinForms.ContextMenuStrip menu)
    {
        foreach (var item in menu.Items.OfType<WinForms.ToolStripMenuItem>())
        {
            if (item.Text == "IP 方案")
                RebuildProfileSubmenu(item);
            else if (item.Text == "Wi-Fi")
                RebuildWifiSubmenu(item);
        }
    }

    private void RebuildProfileSubmenu(WinForms.ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();
        var profiles = _profileStore.Load()
            .OrderBy(p => p.AdapterName, StringComparer.CurrentCulture)
            .ThenBy(p => p.Name, StringComparer.CurrentCulture)
            .ToList();
        if (profiles.Count == 0)
        {
            parent.DropDownItems.Add("(暂无方案,请先到「IP 方案」页创建)").Enabled = false;
            return;
        }
        foreach (var profile in profiles)
        {
            var mi = parent.DropDownItems.Add($"{profile.Name}(网卡:{profile.AdapterName})");
            mi.Click += async (_, _) => await ApplyProfileFromTrayAsync(profile);
        }
    }

    private void RebuildWifiSubmenu(WinForms.ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();
        parent.DropDownItems.Add(_wifi.CurrentWifiText.Length == 0 ? "未连接 Wi-Fi" : _wifi.CurrentWifiText).Enabled = false;
        parent.DropDownItems.Add(new WinForms.ToolStripSeparator());

        // 列表为空或缓存过旧时后台补扫,本次先展示旧数据,下次展开即最新
        if (!_wifi.IsWifiScanning && DateTime.Now - _wifi.LastScanAt > TimeSpan.FromMinutes(5))
            _ = _wifi.RefreshAsync();

        var networks = _wifi.Networks.Take(15).ToList();
        if (networks.Count == 0)
        {
            parent.DropDownItems.Add(_wifi.IsWifiScanning ? "正在扫描附近的 Wi-Fi…" : "(暂无扫描结果)")
                .Enabled = false;
            return;
        }
        foreach (var network in networks)
        {
            var label = network.Connected ? $"✓ {network.Ssid}" : $"{network.Ssid}({network.Signal}%)";
            var mi = parent.DropDownItems.Add(label);
            mi.Click += async (_, _) => await ConnectWifiFromTrayAsync(network.Ssid);
        }
    }

    private async Task ApplyProfileFromTrayAsync(IpProfile profile)
    {
        var (ok, message) = await Task.Run(() => _network.ApplyProfileAsync(profile)).ConfigureAwait(true);
        ShowBalloon(ok ? "IP 方案已切换" : "切换失败", message);
    }

    /// <summary>托盘连 Wi-Fi 不弹密码框,直接用本机已保存的配置;没保存过的到 Wi-Fi 页连接。</summary>
    private async Task ConnectWifiFromTrayAsync(string ssid)
    {
        ShowBalloon("Wi-Fi", $"正在连接「{ssid}」…");
        var (ok, message) = await Task.Run(() => WlanService.ConnectAsync(ssid, null)).ConfigureAwait(true);
        if (!ok)
        {
            ShowBalloon("连接失败", message);
            return;
        }
        var connected = await Task.Run(() => WlanService.WaitConnectedAsync(ssid)).ConfigureAwait(true);
        ShowBalloon(connected ? "Wi-Fi 已连接" : "连接未完成",
            connected
                ? ssid
                : $"「{ssid}」连接超时(该网络可能没保存过密码,请到 Wi-Fi 页连接一次)");
        if (connected)
            _ = _wifi.RefreshAsync();
    }

    private void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(3000, title, text, WinForms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
