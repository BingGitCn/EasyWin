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

    public List<IpProfile> Load() => LoadFrom(StorePath);

    /// <summary>主存储写入,保持临时文件 + 原子替换,避免写一半损坏。</summary>
    public void Save(IReadOnlyList<IpProfile> profiles)
    {
        var json = JsonSerializer.Serialize(profiles, JsonOptions);
        var temp = StorePath + ".tmp";
        File.WriteAllText(temp, json, System.Text.Encoding.UTF8);
        File.Move(temp, StorePath, overwrite: true);
        Log.Info($"已保存 {profiles.Count} 个配置方案 → {StorePath}");
    }

    /// <summary>从任意路径读取方案(导入用),文件损坏时返回空列表。</summary>
    public List<IpProfile> LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<IpProfile>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            Log.Error($"读取方案文件失败: {path}", ex);
            return [];
        }
    }

    public void SaveTo(string path, IReadOnlyList<IpProfile> profiles) =>
        File.WriteAllText(path, JsonSerializer.Serialize(profiles, JsonOptions), System.Text.Encoding.UTF8);
}
