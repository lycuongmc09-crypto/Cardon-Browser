using System.IO;
using System.Text.Json;

namespace KmyBrowser.Services;

// PerfList v0 — "EasyList cho performance": xuất tri thức máy này (CPR resources,
// PRISM heights) thành file JSON chia sẻ được; máy mới import là có ngay,
// không cần 4 lần ghé để học (xóa cold-start).
// Chống đầu độc: seed HitRate capped dưới cổng (0.79 < 0.8) — cần đúng 1 lần ghé
// thật để xác nhận mới được preload. Heights vô hại nên seed thẳng.
public static class PerfList
{
    public sealed class ResEntry
    {
        public string U { get; set; } = "";
        public string K { get; set; } = "";
        public int D { get; set; }
        public double H { get; set; }
        public int B { get; set; }
    }

    public sealed class HeightEntry
    {
        public string Fp { get; set; } = "";
        public double H { get; set; }
    }

    public sealed class RouteEntry
    {
        public int Visits { get; set; }
        public List<ResEntry> Resources { get; set; } = new();
        public List<HeightEntry> Heights { get; set; } = new();
    }

    public sealed class ListFile
    {
        public int Version { get; set; } = 1;
        public string ExportedAt { get; set; } = "";
        public Dictionary<string, RouteEntry> Routes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    public const double SeedHitCap = 0.79;

    public static string DefaultExportPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CardonBrowser", "perflist-export.json");

    public static string DefaultImportPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CardonBrowser", "perflist-import.json");

    public static ListFile Build(
        List<(string route, CprStore.RouteData data)> cpr,
        List<(string route, PrismLedger.RouteLedger data)> prism)
    {
        var f = new ListFile { ExportedAt = DateTime.UtcNow.ToString("o") };
        foreach (var (route, d) in cpr)
        {
            var e = new RouteEntry { Visits = d.Loads };
            foreach (var r in d.Resources.Values)
                e.Resources.Add(new ResEntry { U = r.Url, K = r.Kind, D = r.Depth, H = Math.Round(r.HitRate, 3), B = r.Bytes });
            f.Routes[route] = e;
        }
        foreach (var (route, d) in prism)
        {
            if (!f.Routes.TryGetValue(route, out var e))
            {
                e = new RouteEntry();
                f.Routes[route] = e;
            }
            e.Visits = Math.Max(e.Visits, d.Visits);
            foreach (var kv in d.Segments)
                e.Heights.Add(new HeightEntry { Fp = kv.Key, H = Math.Round(kv.Value.HeightEwma, 1) });
        }
        return f;
    }

    public static void ExportTo(
        List<(string route, CprStore.RouteData data)> cpr,
        List<(string route, PrismLedger.RouteLedger data)> prism,
        string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(Build(cpr, prism), Opts));
        }
        catch { }
    }

    // Trả về (routes, resources, heights) đã seed để hiển thị.
    public static (int routes, int res, int heights) ImportFrom(
        string path, CprStore cpr, PrismLedger prism)
    {
        int routes = 0, res = 0, heights = 0;
        try
        {
            if (!System.IO.File.Exists(path)) return (0, 0, 0);
            var f = JsonSerializer.Deserialize<ListFile>(System.IO.File.ReadAllText(path));
            if (f is null || f.Version != 1) return (0, 0, 0);
            foreach (var (route, e) in f.Routes)
            {
                routes++;
                var seeds = e.Resources
                    .Where(r => r.U.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    .Select(r => (r.U, r.K, r.D, Math.Min(r.H, SeedHitCap), r.B))
                    .ToList();
                res += cpr.ImportSeed(route, seeds);
                heights += prism.ImportSeed(route, e.Heights.Select(h => (h.Fp, h.H)).ToList());
            }
        }
        catch { }
        return (routes, res, heights);
    }
}
