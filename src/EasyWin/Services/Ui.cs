using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace EasyWin.Services;

/// <summary>Fluent 风格弹窗(确认/提示),替代系统 MessageBox。</summary>
public static class Ui
{
    /// <summary>确认弹窗:返回 true 表示用户点了主按钮。</summary>
    public static Task<bool> ConfirmAsync(string title, string message, string extraDetail = "",
        string primaryText = "是", string secondaryText = "否")
    {
        return ConfirmCoreAsync(title, message, extraDetail, primaryText, secondaryText);
    }

    private static async Task<bool> ConfirmCoreAsync(string title, string message, string detail,
        string primaryText, string secondaryText)
    {
        var result = await ShowBoxAsync(title, message, detail, primaryText, secondaryText,
            ControlAppearance.Primary, "Checkmark24");
        return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    /// <summary>警告提示弹窗(仅一个确定按钮)。</summary>
    public static Task AlertAsync(string title, string message)
    {
        return ShowBoxAsync(title, message, "", "知道了", "", ControlAppearance.Caution, "Warning24");
    }

    private static async Task<Wpf.Ui.Controls.MessageBoxResult> ShowBoxAsync(
        string title, string message, string detail, string primaryText, string secondaryText,
        ControlAppearance appearance, string primaryIcon)
    {
        var content = new StackPanel { Orientation = Orientation.Vertical };
        content.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = message,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400,
        });
        if (!string.IsNullOrEmpty(detail))
        {
            content.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = detail,
                FontSize = 12,
                LineHeight = 18,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 400,
                Margin = new Thickness(0, 10, 0, 0),
                // 跟随主题取次要文字色,硬编码浅色会在浅色主题下不可读
                Foreground = System.Windows.Application.Current?.TryFindResource("TextFillColorSecondaryBrush") as Brush
                             ?? new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
            });
        }

        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryText,
            PrimaryButtonAppearance = appearance,
            PrimaryButtonIcon = new SymbolIcon { Symbol = (SymbolRegular)Enum.Parse(typeof(SymbolRegular), primaryIcon) },
        };
        if (!string.IsNullOrEmpty(secondaryText))
        {
            box.SecondaryButtonText = secondaryText;
            box.SecondaryButtonAppearance = ControlAppearance.Secondary;
        }

        return await box.ShowDialogAsync(true);
    }
}
