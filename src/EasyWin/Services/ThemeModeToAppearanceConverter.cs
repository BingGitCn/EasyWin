using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace EasyWin.Services;

/// <summary>当前主题模式与 ConverterParameter 一致时返回 Primary 按钮,否则 Secondary(主题三态切换按钮用)。</summary>
public class ThemeModeToAppearanceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? ControlAppearance.Primary
            : ControlAppearance.Secondary;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
