using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace EasyWin.Services;

/// <summary>Wi-Fi 连接二维码:生成手机相机可扫的 WIFI: URI 并渲染为 PNG。</summary>
public static class WifiQrCodeService
{
    /// <summary>WIFI: URI 标准转义:\ ; , : " 需要前缀反斜杠。</summary>
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
             .Replace(":", "\\:").Replace("\"", "\\\"");

    /// <summary>构造 WIFI: 连接串(开放网络 T:nopass 且省略密码段)。</summary>
    public static string BuildPayload(string ssid, string? password) =>
        string.IsNullOrEmpty(password)
            ? $"WIFI:T:nopass;S:{Escape(ssid)};;"
            : $"WIFI:T:WPA;S:{Escape(ssid)};P:{Escape(password)};;";

    /// <summary>生成二维码 PNG 字节(纯托管渲染,失败返回 null)。</summary>
    public static byte[]? GeneratePng(string ssid, string? password, int pixelsPerModule = 10)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(BuildPayload(ssid, password), QRCodeGenerator.ECCLevel.M);
            using var qr = new PngByteQRCode(data);
            return qr.GetGraphic(pixelsPerModule);
        }
        catch (Exception ex)
        {
            Log.Warn("生成 Wi-Fi 二维码失败: " + ex.Message);
            return null;
        }
    }

    /// <summary>PNG 字节 → WPF 可绑定的 BitmapImage。</summary>
    public static BitmapImage? ToImage(byte[]? png)
    {
        if (png == null || png.Length == 0) return null;
        try
        {
            var image = new BitmapImage();
            using var stream = new MemoryStream(png);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
