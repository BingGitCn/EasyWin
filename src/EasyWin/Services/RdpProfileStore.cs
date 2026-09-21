using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using EasyWin.Models;

namespace EasyWin.Services;

/// <summary>远程桌面连接方案的持久化(rdp.json)。</summary>
public class RdpProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string StorePath => AppPaths.RdpProfilesPath;

    public List<RdpProfile> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return [];
            return JsonSerializer.Deserialize<List<RdpProfile>>(File.ReadAllText(StorePath), JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            Log.Error("读取远程桌面方案失败", ex);
            return [];
        }
    }

    /// <summary>从任意路径读取连接方案(导入用),文件损坏时返回空列表。</summary>
    public List<RdpProfile> LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<RdpProfile>>(File.ReadAllText(path), JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            Log.Error($"读取连接方案文件失败: {path}", ex);
            return [];
        }
    }

    /// <summary>导出到任意路径(不含原子写,仅用于用户指定的导出文件)。</summary>
    public void SaveTo(string path, IReadOnlyList<RdpProfile> profiles) =>
        File.WriteAllText(path, JsonSerializer.Serialize(profiles, JsonOptions), System.Text.Encoding.UTF8);

    public void Save(IReadOnlyList<RdpProfile> profiles)
    {
        var json = JsonSerializer.Serialize(profiles, JsonOptions);
        var temp = StorePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, StorePath, overwrite: true);
        Log.Info($"已保存 {profiles.Count} 个远程桌面方案 → {StorePath}");
    }
}
