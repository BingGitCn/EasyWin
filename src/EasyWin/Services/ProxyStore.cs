using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using EasyWin.Models;

namespace EasyWin.Services;

/// <summary>代理方案的持久化(proxies.json)。</summary>
public class ProxyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string StorePath => Path.Combine(AppPaths.DataDir, "proxies.json");

    public List<ProxyProfile> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return [];
            return JsonSerializer.Deserialize<List<ProxyProfile>>(File.ReadAllText(StorePath), JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            Log.Error("读取代理方案失败", ex);
            return [];
        }
    }

    public void Save(IReadOnlyList<ProxyProfile> proxies)
    {
        var json = JsonSerializer.Serialize(proxies, JsonOptions);
        var temp = StorePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, StorePath, overwrite: true);
        Log.Info($"已保存 {proxies.Count} 个代理方案 → {StorePath}");
    }
}
