using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EasyWin.Services;

/// <summary>布尔取反转换器(用于 DHCP/静态 互斥单选)。</summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
}

/// <summary>非空显示转换器:null 或空串时隐藏元素。</summary>
public class NotNullToVisConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool → "已记住"/"连接时输入"(远程桌面凭据徽章)。</summary>
public class BoolToRememberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "已记住" : "连接时输入";

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
