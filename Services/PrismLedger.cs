using System.IO;
using System.Text.Json;

namespace KmyBrowser.Services;

// PRISM P1 — Geometry Ledger: học chiều cao từng khúc trang theo lần ghé,
// CHƯA cắt/sửa trang (measure-only, zero rủi ro). P2 mới dùng Ledger để
// thay segment bằng placeholder đúng height (CLS=0) + Byte Pool.
// Persist: %AppData%/CardonBrowser/prism.json
public sealed class PrismLedger
{
    public sealed class SegmentGeo
    {
        public double HeightEwma { get; set; }
        public int Visits { get; set; }
        public bool Mutated { get; set; }
        public bool Incompatible { get; set; }
    }

    public sealed class RouteLedger
    {
        public Dictionary<string, SegmentGeo> Segments { get; set; } = new();
        public int Visits { get; set; }
    }

    // Mô tả 1 block do JS đo (giới hạn 200 block, 1 lần sau load).
    public sealed record BlockDesc(string Tag, string Cls, int Kids, int HtmlLen,
        double Top, double Height, bool HasScript, bool HasForm);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CardonBrowser", "prism.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private const double Alpha = 0.4;
    private const int MaxRoutes = 60;
    private const int MaxSegPerRoute = 200;

    private readonly Dictionary<string, RouteLedger> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public void Load()
    {
        try
        {
            lock (_lock)
            {
                _routes.Clear();
                if (!File.Exists(FilePath)) return;
                var all = JsonSerializer.Deserialize<Dictionary<string, RouteLedger>>(
                    File.ReadAllText(FilePath));
                if (all is null) return;
                foreach (var kv in all.Take(MaxRoutes)) _routes[kv.Key] = kv.Value;
            }
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            Dictionary<string, RouteLedger> snap;
            lock (_lock) snap = new Dictionary<string, RouteLedger>(_routes, StringComparer.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(snap, JsonOpts));
        }
        catch { }
    }

    // Fingerprint ổn định qua content thay đổi nhẹ:
    // tag + class tokens (sắp xếp) + bucket độ dài text. Cùng cấu trúc/khác chữ => cùng fp.
    public static string Fingerprint(string tag, string cls, int textLen)
    {
        string toks = string.Join(".", (cls ?? "").Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.TrimStart('.', '#').ToLowerInvariant())
            .Where(t => t.Length > 0 && !t.StartsWith("prism"))
            .OrderBy(t => t).Take(4));
        int bucket = textLen switch { < 200 => 0, < 1000 => 1, < 5000 => 2, < 20000 => 3, _ => 4 };
        string raw = $"{tag.ToLowerInvariant()}|{toks}|b{bucket}";
        uint h = 0x811c9dc5;
        foreach (char c in raw) { h ^= c; h *= 0x01000193; }
        return h.ToString("x8");
    }

    // Quy tắc eligible P2 (dùng ngay P1 để ước tính "PRISM sẽ defer được bao nhiêu").
    public static bool Eligible(BlockDesc b) =>
        (b.HtmlLen >= 8192 || b.Kids >= 200)
        && !b.HasScript && !b.HasForm;

    public void Learn(string route, List<BlockDesc> blocks)
    {
        lock (_lock)
        {
            if (!_routes.TryGetValue(route, out var rl))
            {
                if (_routes.Count >= MaxRoutes) return;
                rl = new RouteLedger();
                _routes[route] = rl;
            }
            rl.Visits++;
            // Cùng fingerprint trong 1 trang (section lặp) -> phân biệt bằng thứ tự xuất hiện.
            var occ = new Dictionary<string, int>();
            foreach (var b in blocks)
            {
                string fp0 = Fingerprint(b.Tag, b.Cls, b.HtmlLen);
                occ.TryGetValue(fp0, out int k);
                occ[fp0] = k + 1;
                string fp = fp0 + "#" + k;
                if (!rl.Segments.TryGetValue(fp, out var g))
                {
                    if (rl.Segments.Count >= MaxSegPerRoute) continue;
                    g = new SegmentGeo();
                    rl.Segments[fp] = g;
                }
                g.Visits++;
                g.HeightEwma = g.Visits == 1 ? b.Height : g.HeightEwma * (1 - Alpha) + b.Height * Alpha;
            }
        }
    }

    public double? HeightFor(string route, string fp)
    {
        lock (_lock)
            return _routes.TryGetValue(route, out var rl) && rl.Segments.TryGetValue(fp, out var g)
                ? g.HeightEwma : null;
    }

    public List<(string route, RouteLedger data)> Snapshot()
    {
        lock (_lock)
            return _routes.OrderByDescending(kv => kv.Value.Visits)
                .Take(20).Select(kv => (kv.Key, kv.Value)).ToList();
    }
}
