using System.IO;
using System.Text.Json;

namespace KmyBrowser.Services;

// CPR P1 — Critical Path Replay ở tầng host.
// Học đồ thị phụ thuộc tài nguyên theo route qua các lần ghé (performance API,
// depth là XẤP XỈ theo initiatorType — CDP initiator thật để dành P2),
// rồi lần sau chèn <link rel=preload> ngay lúc document tạo
// (AddScriptToExecuteOnDocumentCreated), thay vì đợi khám phá từng tầng.
// Persist: %AppData%/CardonBrowser/cpr.json
public sealed class CprStore
{
    public sealed record Res(string Url, string Kind, int Depth, double HitRate, int Bytes);
    public sealed record Preload(string Url, string Rel, string As, bool CrossOrigin);

    // DTO persist — public để JsonSerializer đọc/ghi.
    public sealed class RouteData
    {
        public Dictionary<string, Res> Resources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public int Loads { get; set; }
        public double BaselineMs { get; set; }
        public double SavedMs { get; set; }
        public double HostRttMs { get; set; } = 100;
    }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CardonBrowser", "cpr.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private const double Alpha = 0.35;   // EWMA: resource mới cần ~4 lần ghé để qua cổng 0.8
    private const double Gate = 0.8;
    private const int MaxRoutes = 100;
    private const int MaxResPerRoute = 100;

    // Giữ lại query làm thay đổi nội dung phân trang/ngôn ngữ; còn lại bỏ
    // (utm_*, session, nonce... khiến route key miss 100%).
    private static readonly HashSet<string> KeepQuery = new(StringComparer.OrdinalIgnoreCase)
        { "page", "lang", "tab", "sort" };

    private readonly Dictionary<string, RouteData> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public int RouteCount { get { lock (_lock) return _routes.Count; } }

    public void Load()
    {
        try
        {
            lock (_lock)
            {
                _routes.Clear();
                if (!File.Exists(FilePath)) return;
                var all = JsonSerializer.Deserialize<Dictionary<string, RouteData>>(
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
            Dictionary<string, RouteData> snap;
            lock (_lock) snap = new Dictionary<string, RouteData>(_routes, StringComparer.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(snap, JsonOpts));
        }
        catch { }
    }

    // Chuẩn hóa route: host + path template (/p/123 -> /p/:id), bỏ query ngẫu nhiên.
    public static string NormalizeRoute(string url)
    {
        try
        {
            var u = new Uri(url);
            string host = u.Host.ToLowerInvariant();
            var segs = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => IsIdSegment(s) ? ":id" : s);
            string path = "/" + string.Join("/", segs);
            var keep = new List<string>();
            if (!string.IsNullOrEmpty(u.Query))
            {
                foreach (var kv in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var k = kv.Split('=')[0];
                    if (KeepQuery.Contains(k)) keep.Add(kv);
                }
            }
            keep.Sort(StringComparer.OrdinalIgnoreCase);
            return host + path + (keep.Count > 0 ? "?" + string.Join("&", keep) : "");
        }
        catch { return (url ?? "").Trim().ToLowerInvariant(); }
    }

    private static bool IsIdSegment(string s)
    {
        if (s.All(char.IsDigit)) return true;                       // /123
        if (s.Length is 24 or 32 or 36 && s.All(c => char.IsAsciiHexDigit(c) || c == '-'))
            return true;                                            // objectid / guid
        if (s.Length >= 8 && s.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            && s.Any(char.IsDigit) && !s.Any(char.IsAsciiLetterUpper))
            return s.Length <= 40;                                  // slug-id heuristic
        return false;
    }

    // Depth XẤP XỈ từ initiatorType (performance API không có chuỗi initiator thật).
    public static int ApproxDepth(string initiatorType) => initiatorType switch
    {
        "link" or "script" or "img" => 1,   // preload scanner của Chromium thường tự thấy
        "css" => 2,                          // font/ảnh lồng trong CSS
        "xmlhttprequest" or "fetch" => 3,    // API GET của SPA: chuỗi sâu nhất, giá trị nhất
        _ => 2,
    };

    public static string Classify(string url, string initiatorType)
    {
        string u = url.ToLowerInvariant();
        // Beacon/log/analytics: ổn định nhưng preload chỉ tốn băng thông, không cứu critical path.
        if (u.Contains("/log") || u.Contains("beacon") || u.Contains("track") || u.Contains("pixel")
            || u.Contains("gen_204") || u.Contains("client_204") || u.Contains("analytics")
            || u.Contains("collect?") || u.Contains("/ping"))
            return "beacon";
        if (u.Contains(".woff2") || u.Contains(".woff") || u.Contains(".ttf") || u.Contains("font"))
            return "font";
        if (u.EndsWith(".css")) return "css";
        if (initiatorType == "css") return "css-img";
        if (initiatorType is "xmlhttprequest" or "fetch") return "fetch-GET";
        if (u.EndsWith(".js") || u.EndsWith(".mjs")) return "chunk";
        if (u.Contains(".png") || u.Contains(".jpg") || u.Contains(".webp") || u.Contains(".svg"))
            return "image";
        return "other";
    }

    // Học 1 lần load: entries = (url, initiatorType, bytes).
    public void Learn(string pageUrl, List<(string url, string initiator, int bytes)> entries,
        double hostRttMs = 0)
    {
        string route = NormalizeRoute(pageUrl);
        lock (_lock)
        {
            if (!_routes.TryGetValue(route, out var rd))
            {
                if (_routes.Count >= MaxRoutes) return;
                rd = new RouteData();
                _routes[route] = rd;
            }
            rd.Loads++;
            if (hostRttMs > 5 && hostRttMs < 5000)
                rd.HostRttMs = rd.HostRttMs * (1 - Alpha) + hostRttMs * Alpha;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (url, initiator, bytes) in entries)
            {
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                if (seen.Contains(url) || rd.Resources.Count >= MaxResPerRoute + seen.Count) continue;
                seen.Add(url);
                int depth = ApproxDepth(initiator);
                string kind = Classify(url, initiator);
                if (rd.Resources.TryGetValue(url, out var old))
                    rd.Resources[url] = old with
                    {
                        HitRate = old.HitRate * (1 - Alpha) + Alpha,
                        Bytes = bytes > 0 ? bytes : old.Bytes,
                        Depth = depth, Kind = kind
                    };
                else
                    rd.Resources[url] = new Res(url, kind, depth, Alpha, Math.Max(0, bytes));
            }
            // Resource cũ không xuất hiện nữa -> decay (trang đổi, tránh preload xác chết).
            foreach (var k in rd.Resources.Keys.ToList())
                if (!seen.Contains(k))
                {
                    var old = rd.Resources[k];
                    double h = old.HitRate * (1 - Alpha);
                    if (h < 0.05) rd.Resources.Remove(k);
                    else rd.Resources[k] = old with { HitRate = h };
                }
        }
    }

    public void NoteTiming(string pageUrl, double elapsedMs, bool replayUsed)
    {
        string route = NormalizeRoute(pageUrl);
        lock (_lock)
        {
            if (!_routes.TryGetValue(route, out var rd)) return;
            if (!replayUsed)
                rd.BaselineMs = rd.BaselineMs == 0 ? elapsedMs : rd.BaselineMs * 0.7 + elapsedMs * 0.3;
            else if (rd.BaselineMs > 0)
                rd.SavedMs = rd.SavedMs * 0.7 + Math.Max(0, rd.BaselineMs - elapsedMs) * 0.3;
        }
    }

    // Điểm preload: chỉ depth>=2 (scanner không tự thấy) và đủ ổn định.
    public static double Score(Res r, double rttMs)
    {
        if (r.Depth < 2 || r.HitRate < Gate) return 0;
        double saved = (r.Depth - 1) * rttMs;
        return r.HitRate * saved / (r.Bytes + 4096);
    }

    // Top preload trong ngân sách byte, sắp xếp theo điểm giảm dần.
    public List<Preload> GetPreloads(string pageUrl, int byteBudget = 150_000)
    {
        string route = NormalizeRoute(pageUrl);
        lock (_lock)
        {
            if (!_routes.TryGetValue(route, out var rd)) return new();
            return rd.Resources.Values
                .Select(r => (r, s: Score(r, rd.HostRttMs)))
                .Where(t => t.s > 0 && t.r.Kind is "font" or "css" or "css-img" or "fetch-GET" or "chunk" or "image")
                .OrderByDescending(t => t.s)
                .Aggregate((list: new List<Preload>(), used: 0),
                    (acc, t) =>
                    {
                        if (acc.used + t.r.Bytes <= byteBudget)
                        {
                            acc.list.Add(ToPreload(t.r));
                            acc.used += t.r.Bytes;
                        }
                        return acc;
                    }).list;
        }
    }

    private static Preload ToPreload(Res r) => r.Kind switch
    {
        "font" => new Preload(r.Url, "preload", "font", true),
        "css" => new Preload(r.Url, "preload", "style", false),
        "chunk" => new Preload(r.Url, "modulepreload", "", false),
        "fetch-GET" => new Preload(r.Url, "preload", "fetch", true),
        _ => new Preload(r.Url, "preload", "image", false),
    };

    public List<(string route, RouteData data)> Snapshot()
    {
        lock (_lock)
            return _routes.OrderByDescending(kv => kv.Value.Loads)
                .Take(30).Select(kv => (kv.Key, kv.Value)).ToList();
    }
}
