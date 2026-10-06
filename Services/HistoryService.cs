using System.IO;
using System.Text.Json;
using KmyBrowser.Models;

namespace KmyBrowser.Services;

// CUSTOMIZE: đổi path lưu file tại DataDir. Mặc định %AppData%/KmyBrowser/
public sealed class HistoryService
{
    private static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CardonBrowser");
    private static readonly string FilePath = Path.Combine(DataDir, "history.json");

    private readonly List<HistoryEntry> _entries = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public IReadOnlyList<HistoryEntry> Entries => _entries;

    public void Load()
    {
        try
        {
            _entries.Clear();
            if (!File.Exists(FilePath)) return;
            var json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<HistoryEntry>>(json);
            if (list is null) return;
            // mới nhất lên đầu, giới hạn 1000 dòng để file nhẹ
            _entries.AddRange(list.OrderByDescending(e => e.VisitedAt).Take(1000));
        }
        catch { /* file hỏng -> bắt đầu trống, không crash */ }
    }

    public void Add(string title, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
        // tránh spam trùng liên tiếp
        if (_entries.Count > 0 && _entries[0].Url == url) return;
        _entries.Insert(0, new HistoryEntry
        {
            Title = string.IsNullOrWhiteSpace(title) ? url : title,
            Url = url,
            VisitedAt = DateTime.Now
        });
        if (_entries.Count > 1000) _entries.RemoveRange(1000, _entries.Count - 1000);
        Save();
    }

    public void Clear()
    {
        _entries.Clear();
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_entries, JsonOpts));
        }
        catch { /* bỏ qua lỗi ghi file */ }
    }
}
