using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyWin.Services;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

/// <summary>轻量设置(主题、电源自动切换等),存数据目录 settings.json。</summary>
public static class SettingsStore
{
    private static string Path => System.IO.Path.Combine(AppPaths.DataDir, "settings.json");

    private static readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    private sealed class Data
    {
        public string ThemeMode { get; set; } = "dark";
        public bool DarkTheme { get; set; } = true;
        public bool PowerAutoEnabled { get; set; }
        public string AcPlanGuid { get; set; } = "";
        public string BatteryPlanGuid { get; set; } = "";
    }

    private static Data _data = Load();

    private static Data Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(Path), JsonOptions);
                if (data != null) return data;
            }
        }
        catch { /* 损坏则用默认 */ }
        return new Data();
    }

    private static void Save()
    {
        lock (_lock)
        {
            try
            {
                File.WriteAllText(Path, JsonSerializer.Serialize(_data, JsonOptions));
            }
            catch (Exception ex)
            {
                Log.Warn("保存设置失败: " + ex.Message);
            }
        }
    }

    // ---------------- 主题 ----------------

    /// <summary>主题模式:跟随系统 / 浅色 / 深色。默认深色(与历史版本一致);旧版 settings.json 的 darkTheme 字段兼容读取。</summary>
    public static ThemeMode Mode
    {
        get => _data.ThemeMode.ToLowerInvariant() switch
        {
            "system" => ThemeMode.System,
            "light" => ThemeMode.Light,
            _ => ThemeMode.Dark
        };
        set
        {
            _data.ThemeMode = value.ToString().ToLowerInvariant();
            _data.DarkTheme = IsDark; // 旧字段一并写,老版本可读
            Save();
        }
    }

    /// <summary>解析后的深色开关;跟随系统时读 Windows 个性化设置。托盘菜单配色等场景使用。</summary>
    public static bool IsDark => Mode switch
    {
        ThemeMode.Light => false,
        ThemeMode.Dark => true,
        _ => !SystemPrefersLight()
    };

    /// <summary>深色为 true(旧属性名,语义同 IsDark)。</summary>
    public static bool Theme => IsDark;

    private static bool SystemPrefersLight()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is 1;
        }
        catch
        {
            return false;
        }
    }

    // ---------------- 电源计划自动切换 ----------------

    /// <summary>按供电状态自动切换电源计划的开关。</summary>
    public static bool PowerAutoEnabled
    {
        get => _data.PowerAutoEnabled;
        set { _data.PowerAutoEnabled = value; Save(); }
    }

    /// <summary>接通电源时使用的计划 GUID(空 = 未选择)。</summary>
    public static string AcPlanGuid
    {
        get => _data.AcPlanGuid;
        set { _data.AcPlanGuid = value; Save(); }
    }

    /// <summary>使用电池时使用的计划 GUID(空 = 未选择)。</summary>
    public static string BatteryPlanGuid
    {
        get => _data.BatteryPlanGuid;
        set { _data.BatteryPlanGuid = value; Save(); }
    }
}
