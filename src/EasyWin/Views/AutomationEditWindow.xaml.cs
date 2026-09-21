using System.Windows;
using EasyWin.Models;
using EasyWin.Services;
using Wpf.Ui.Controls;

namespace EasyWin.Views;

public partial class AutomationEditWindow : FluentWindow
{
    private readonly AutomationRule _rule;

    /// <summary>点击保存后包含编辑结果;取消时为 null。</summary>
    public AutomationRule? Result { get; private set; }

    public AutomationEditWindow(AutomationRule rule, IReadOnlyList<IpProfile> profiles, IReadOnlyList<string> ssidCandidates)
    {
        InitializeComponent();
        _rule = rule;
        Title = string.IsNullOrEmpty(rule.Ssid) ? "新建自动化规则" : "编辑自动化规则";

        SsidBox.ItemsSource = ssidCandidates;
        SsidBox.Text = rule.Ssid;

        ProfileBox.ItemsSource = profiles;
        ProfileBox.DisplayMemberPath = nameof(IpProfile.Name);
        ProfileBox.SelectedValuePath = nameof(IpProfile.Id);
        ProfileBox.SelectedValue = rule.ProfileId == Guid.Empty ? profiles.FirstOrDefault()?.Id : rule.ProfileId;

        EnabledBox.IsChecked = rule.Enabled;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var ssid = SsidBox.Text.Trim();
        if (ssid.Length == 0)
        {
            await Ui.AlertAsync("请填写 Wi-Fi 名称", "规则需要指定触发条件的 Wi-Fi(SSID)。");
            return;
        }
        if (ProfileBox.SelectedValue is not Guid profileId)
        {
            await Ui.AlertAsync("请选择方案", "规则需要指定命中后要应用的 IP 方案。");
            return;
        }

        _rule.Ssid = ssid;
        _rule.ProfileId = profileId;
        _rule.Enabled = EnabledBox.IsChecked == true;
        Result = _rule;
        DialogResult = true;
    }
}
