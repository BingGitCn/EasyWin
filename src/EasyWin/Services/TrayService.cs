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
    private readonly WinForms.ContextMenuStrip _menu;
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

        _menu = new WinForms.ContextMenuStrip
        {
            ShowImageMargin = false, // 不留图片条,菜单更紧凑
        };
        _menu.Items.Add("打开 EasyWin", null, (_, _) => OpenRequested?.Invoke());
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add("Wi-Fi", null, null);      // 高频操作放前面
        _menu.Items.Add("IP 方案", null, null);    // 子菜单项在每次展开时动态重建
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke());
        _menu.Opening += OnMenuOpening;

        _icon.ContextMenuStrip = _menu;
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) OpenRequested?.Invoke(); };
    }

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        RebuildDynamicItems(_menu);
        ApplyMenuTheme(); // 重建后再上色,动态生成的子项也能覆盖到
    }

    /// <summary>菜单配色跟随应用主题。WinForms 菜单默认是浅色系统样式,与 Fluent 界面不搭,这里整体重画。</summary>
    private void ApplyMenuTheme()
    {
        var dark = SettingsStore.Theme;
        _menu.Renderer = new FluentMenuRenderer(dark);
        PaintItems(_menu.Items, dark);
    }

    private static void PaintItems(WinForms.ToolStripItemCollection items, bool dark)
    {
        foreach (var item in items.OfType<WinForms.ToolStripMenuItem>())
        {
            item.ForeColor = dark ? Drawing.Color.FromArgb(0xF2, 0xF2, 0xF2) : Drawing.Color.FromArgb(0x1B, 0x1B, 0x1B);
            PaintItems(item.DropDownItems, dark);
        }
    }

    /// <summary>Fluent 风格菜单渲染:纯色底、细边框、悬停浅色叠加,深浅色随应用主题。</summary>
    private sealed class FluentMenuRenderer : WinForms.ToolStripProfessionalRenderer
    {
        private readonly bool _dark;

        public FluentMenuRenderer(bool dark) : base(new MenuColorTable(dark))
        {
            _dark = dark;
            RoundedEdges = false;
        }

        protected override void OnRenderMenuItemBackground(WinForms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed)
                return; // 不画基类渐变,未悬停时保持纯色底
            var rect = new Drawing.Rectangle(1, 1, e.Item.Width - 2, e.Item.Height - 2);
            var overlay = Drawing.Color.FromArgb(_dark ? 0x30 : 0x14, 0x88, 0x88, 0x88);
            using var brush = new Drawing.SolidBrush(overlay);
            e.Graphics.FillRectangle(brush, rect);
        }
    }

    private sealed class MenuColorTable : WinForms.ProfessionalColorTable
    {
        private readonly bool _dark;

        public MenuColorTable(bool dark) => _dark = dark;

        private Drawing.Color Background => _dark
            ? Drawing.Color.FromArgb(0x2B, 0x2B, 0x2B)
            : Drawing.Color.FromArgb(0xFC, 0xFC, 0xFC);

        public override Drawing.Color ToolStripDropDownBackground => Background;
        public override Drawing.Color ImageMarginGradientBegin => Background;
        public override Drawing.Color ImageMarginGradientMiddle => Background;
        public override Drawing.Color ImageMarginGradientEnd => Background;
        public override Drawing.Color MenuItemPressedGradientBegin => Background;
        public override Drawing.Color MenuItemPressedGradientEnd => Background;
        public override Drawing.Color MenuItemSelected => _dark
            ? Drawing.Color.FromArgb(0x3A, 0x3A, 0x3A)
            : Drawing.Color.FromArgb(0xE9, 0xE9, 0xE9);
        public override Drawing.Color MenuItemSelectedGradientBegin => MenuItemSelected;
        public override Drawing.Color MenuItemSelectedGradientEnd => MenuItemSelected;
        public override Drawing.Color MenuBorder => _dark
            ? Drawing.Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF)
            : Drawing.Color.FromArgb(0x22, 0x00, 0x00, 0x00);
        public override Drawing.Color MenuItemBorder => Drawing.Color.Transparent;
        public override Drawing.Color SeparatorDark => _dark
            ? Drawing.Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)
            : Drawing.Color.FromArgb(0x14, 0x00, 0x00, 0x00);
        public override Drawing.Color SeparatorLight => SeparatorDark;
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
