using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyWin.Models;
using EasyWin.Services;
using EasyWin.Views;

namespace EasyWin.ViewModels;

/// <summary>远程桌面页:连接方案卡片,一键拉起 mstsc。</summary>
public partial class RdpViewModel : ObservableObject
{
    private readonly RdpProfileStore _store;
    private readonly NetworkService _network;

    public RdpViewModel(RdpProfileStore store, NetworkService network)
    {
        _store = store;
        _network = network;
    }

    public ObservableCollection<RdpProfile> Profiles { get; } = [];

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var profiles = await Task.Run(() => _store.Load()).ConfigureAwait(true);
        Profiles.Clear();
        foreach (var profile in profiles.OrderBy(p => p.Name, StringComparer.CurrentCulture)) Profiles.Add(profile);
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        if (EditProfile(new RdpProfile(), out var saved))
        {
            var all = _store.Load();
            all.Add(saved);
            _store.Save(all);
            Toast.Success($"连接「{saved.Name}」已创建");
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task EditAsync(RdpProfile? profile)
    {
        if (profile == null) return;

        var copy = new RdpProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            Server = profile.Server,
            UserName = profile.UserName,
            RememberPassword = profile.RememberPassword,
            FullScreen = profile.FullScreen,
            Width = profile.Width,
            Height = profile.Height,
            AdminSession = profile.AdminSession,
            CreatedAt = profile.CreatedAt,
        };
        copy.SetPassword(profile.GetPassword());

        if (EditProfile(copy, out var saved))
        {
            var all = _store.Load();
            var index = all.FindIndex(p => p.Id == saved.Id);
            if (index >= 0) all[index] = saved; else all.Add(saved);
            _store.Save(all);
            Toast.Success($"连接「{saved.Name}」已更新");
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(RdpProfile? profile)
    {
        if (profile == null) return;
        if (!await Ui.ConfirmAsync("删除连接", $"确定删除「{profile.Name}」({profile.Server})吗?", "此操作不可恢复。")) return;

        var all = _store.Load();
        all.RemoveAll(p => p.Id == profile.Id);
        _store.Save(all);
        Toast.Success($"连接「{profile.Name}」已删除");
        await RefreshAsync().ConfigureAwait(true);
    }

    // ---------------- 导入 / 导出 ----------------
    // 注意:密码按当前用户 DPAPI 加密,导出到其他机器/用户后无法解密,导入后需重新输入。

    [RelayCommand]
    private void ExportProfiles()
    {
        if (Profiles.Count == 0)
        {
            Toast.Show("还没有连接方案可导出。", ToastType.Info);
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出远程桌面连接",
            Filter = "EasyWin 连接 (*.easywin.json)|*.easywin.json|JSON 文件 (*.json)|*.json",
            FileName = $"easywin-rdp-{DateTime.Now:yyyyMMdd}.json",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _store.SaveTo(dialog.FileName, Profiles.ToList());
            Toast.Success($"已导出 {Profiles.Count} 个连接 → {System.IO.Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            Log.Error("导出远程桌面连接失败", ex);
            Toast.Error("导出失败:" + ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportProfilesAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入远程桌面连接",
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        var imported = _store.LoadFrom(dialog.FileName);
        if (imported.Count == 0)
        {
            await Ui.AlertAsync("导入失败", "文件里没有可识别的连接数据。");
            return;
        }

        var all = _store.Load();
        int added = 0, replaced = 0;
        foreach (var profile in imported)
        {
            var index = all.FindIndex(p => p.Id == profile.Id);
            if (index >= 0) { all[index] = profile; replaced++; }
            else { all.Add(profile); added++; }
        }
        _store.Save(all);
        Toast.Success($"导入完成:新增 {added} 个,覆盖同名 {replaced} 个(跨机器导入需重新输入密码)");
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>一键连接:写凭据 + 生成 .rdp + 拉起 mstsc。</summary>
    [RelayCommand]
    private async Task ConnectAsync(RdpProfile? profile)
    {
        if (profile == null) return;
        var (ok, message) = await Task.Run(() => RdpLauncher.LaunchAsync(profile)).ConfigureAwait(true);
        if (ok) Toast.Success(message);
        else Toast.Error(message);
    }

    /// <summary>打开编辑窗口并等待结果;返回 true 表示用户确认了保存。</summary>
    private bool EditProfile(RdpProfile profile, out RdpProfile saved)
    {
        var window = new RdpEditWindow(profile, _network) { Owner = Application.Current.MainWindow };
        var ok = window.ShowDialog() == true;
        saved = window.Result ?? profile;
        return ok;
    }
}
