using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using EasyWin.Models;

namespace EasyWin.Services;

/// <summary>IP 配置方案的持久化(JSON)。</summary>
public class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文保持可读
        Converters = { new JsonStringEnumConverter() },
    };

    public string StorePath => AppPaths.ProfilesPath;

    public List<IpProfile> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return [];
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<List<IpProfile>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            Log.Error("读取配置方案失败", ex);
            return [];
        }
    }

    public void Save(IReadOnlyList<IpProfile> profiles)
    {
        var json = JsonSerializer.Serialize(profiles, JsonOptions);
        var path = StorePath;
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
        Log.Info($"已保存 {profiles.Count} 个配置方案 → {path}");
    }
}
