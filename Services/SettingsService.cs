using System.IO;
using System.Text.Json;
using KmyBrowser.Models;

namespace KmyBrowser.Services;

// CUSTOMIZE: thêm field mới vào AppSettings là tự có persist.
public sealed class SettingsService
{
    private static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CardonBrowser");
    private static readonly string FilePath = Path.Combine(DataDir, "settings.json");

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { Current = new AppSettings(); return; }
            var json = File.ReadAllText(FilePath);
            Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch { Current = new AppSettings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public string BuildSearchUrl(string query)
    {
        var tpl = Current.SearchUrlTemplate;
        if (!tpl.Contains("{q}")) tpl += "{q}";
        return tpl.Replace("{q}", Uri.EscapeDataString(query));
    }
}
