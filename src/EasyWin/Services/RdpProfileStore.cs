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

    public void Save(IReadOnlyList<RdpProfile> profiles)
    {
        var json = JsonSerializer.Serialize(profiles, JsonOptions);
        var temp = StorePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, StorePath, overwrite: true);
        Log.Info($"已保存 {profiles.Count} 个远程桌面方案 → {StorePath}");
    }
}
