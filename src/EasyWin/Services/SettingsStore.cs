using System.IO;
using System.Text.Json;

namespace EasyWin.Services;

/// <summary>轻量设置(主题等),存数据目录 settings.json。</summary>
public static class SettingsStore
{
    private static string Path => System.IO.Path.Combine(AppPaths.DataDir, "settings.json");

    private static bool? _theme;

    /// <summary>true=深色(默认),false=浅色。</summary>
    public static bool Theme
    {
        get
        {
            if (_theme.HasValue) return _theme.Value;
            try
            {
                if (File.Exists(Path))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(Path));
                    var json = doc.RootElement;
                    if (json.TryGetProperty("darkTheme", out var v))
                    {
                        _theme = v.GetBoolean();
                        return _theme.Value;
                    }
                }
            }
            catch { /* 损坏则用默认 */ }
            return true;
        }
        set
        {
            _theme = value;
            try
            {
                File.WriteAllText(Path, JsonSerializer.Serialize(new { darkTheme = value }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Log.Warn("保存设置失败: " + ex.Message);
            }
        }
    }
}
