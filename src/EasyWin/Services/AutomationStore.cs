using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using EasyWin.Models;

namespace EasyWin.Services;

/// <summary>自动化规则的持久化(automation.json)。</summary>
public class AutomationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string StorePath => Path.Combine(AppPaths.DataDir, "automation.json");

    public List<AutomationRule> Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return [];
            return JsonSerializer.Deserialize<List<AutomationRule>>(File.ReadAllText(StorePath), JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            Log.Error("读取自动化规则失败", ex);
            return [];
        }
    }

    public void Save(IReadOnlyList<AutomationRule> rules)
    {
        var json = JsonSerializer.Serialize(rules, JsonOptions);
        var temp = StorePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, StorePath, overwrite: true);
        Log.Info($"已保存 {rules.Count} 条自动化规则 → {StorePath}");
    }
}
