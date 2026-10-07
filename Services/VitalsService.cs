using System.IO;
using System.Reflection;
using System.Text.Json;

namespace KmyBrowser.Services;

// Quan trắc Web Vitals + CDP thật (thay Stopwatch thô):
// LCP/CLS/INP từ web-vitals.js (postMessage), nodes/jsHeap/layout từ CDP,
// bfcache hit/miss từ event Page.backForwardCacheNotUsed. Persist vitals.json.
public sealed class VitalsService
{
    public sealed class RouteVitals
    {
        public int Loads { get; set; }
        public double LcpMs { get; set; }
        public double Cls { get; set; }
        public double InpMs { get; set; }
        public long JsHeapBytes { get; set; }
        public int Nodes { get; set; }
        public double LayoutCount { get; set; }
        public int BfcacheHits { get; set; }
        public int BfcacheMiss { get; set; }
        public string LastBfcacheReasons { get; set; } = "";
    }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CardonBrowser", "vitals.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private const double Alpha = 0.4;
    private const int MaxRoutes = 100;

    private readonly Dictionary<string, RouteVitals> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public void Load()
    {
        try
        {
            lock (_lock)
            {
                _routes.Clear();
                if (!File.Exists(FilePath)) return;
                var all = JsonSerializer.Deserialize<Dictionary<string, RouteVitals>>(
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
            Dictionary<string, RouteVitals> snap;
            lock (_lock) snap = new Dictionary<string, RouteVitals>(_routes, StringComparer.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(snap, JsonOpts));
        }
        catch { }
    }

    private RouteVitals For(string pageUrl)
    {
        string route = CprStore.NormalizeRoute(pageUrl);
        if (!_routes.TryGetValue(route, out var rv))
        {
            if (_routes.Count >= MaxRoutes) return new RouteVitals();
            rv = new RouteVitals();
            _routes[route] = rv;
        }
        rv.Loads++;
        return rv;
    }

    // Tin postMessage {t:'vital', n:'LCP'|'CLS'|'INP', v:number, u:url}
    public void NoteVital(string pageUrl, string name, double value)
    {
        if (value < 0 || double.IsNaN(value) || double.IsInfinity(value)) return;
        lock (_lock)
        {
            var rv = For(pageUrl);
            switch (name.ToUpperInvariant())
            {
                case "LCP": rv.LcpMs = Ewma(rv.LcpMs, value); break;
                case "CLS": rv.Cls = Ewma(rv.Cls, value); break;
                case "INP": rv.InpMs = Ewma(rv.InpMs, value); break;
            }
        }
    }

    public void NoteCdp(string pageUrl, int nodes, long jsHeap, double layoutCount)
    {
        lock (_lock)
        {
            var rv = For(pageUrl);
            if (nodes > 0) rv.Nodes = nodes;
            if (jsHeap > 0) rv.JsHeapBytes = (long)Ewma(rv.JsHeapBytes, jsHeap);
            if (layoutCount >= 0) rv.LayoutCount = Ewma(rv.LayoutCount, layoutCount);
        }
    }

    public void NoteBfcache(string pageUrl, bool hit, string reasons)
    {
        lock (_lock)
        {
            var rv = For(pageUrl);
            if (hit) rv.BfcacheHits++;
            else { rv.BfcacheMiss++; rv.LastBfcacheReasons = reasons.Length > 120 ? reasons[..120] : reasons; }
        }
    }

    private static double Ewma(double old, double sample) =>
        old == 0 ? sample : old * (1 - Alpha) + sample * Alpha;

    public List<(string route, RouteVitals v)> Snapshot()
    {
        lock (_lock)
            return _routes.OrderByDescending(kv => kv.Value.Loads)
                .Take(30).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    // Parse message web-vitals postMessage -> (name, value, url). null nếu không phải vital.
    public static (string name, double value, string url)? ParseVitalMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (!r.TryGetProperty("t", out var t) || t.GetString() != "vital") return null;
            string n = r.TryGetProperty("n", out var pn) ? pn.GetString() ?? "" : "";
            string u = r.TryGetProperty("u", out var pu) ? pu.GetString() ?? "" : "";
            double v = r.TryGetProperty("v", out var pv) ? pv.GetDouble() : -1;
            if (n is not ("LCP" or "CLS" or "INP") || v < 0) return null;
            return (n, v, u);
        }
        catch { return null; }
    }

    // Parse Performance.getMetrics -> dict name->value.
    public static Dictionary<string, double> ParseMetrics(string json)
    {
        var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("metrics", out var arr)
                && arr.ValueKind == JsonValueKind.Array)
                foreach (var m in arr.EnumerateArray())
                {
                    string n = m.TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : "";
                    double v = m.TryGetProperty("value", out var pv) ? pv.GetDouble() : 0;
                    if (n != "") d[n] = v;
                }
        }
        catch { }
        return d;
    }

    // Parse Memory.getDOMCounters -> (nodes, jsEventListeners).
    public static (int nodes, int listeners) ParseDomCounters(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            int n = r.TryGetProperty("nodes", out var pn) ? pn.GetInt32() : 0;
            int l = r.TryGetProperty("jsEventListeners", out var pl) ? pl.GetInt32() : 0;
            return (n, l);
        }
        catch { return (0, 0); }
    }

    // Parse event Page.backForwardCacheNotUsed -> danh sách reason (có thể rỗng).
    public static List<string> ParseBfcacheReasons(string json)
    {
        var out_ = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("notRestoredExplanations", out var arr)
                && arr.ValueKind == JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray())
                    if (e.TryGetProperty("reason", out var pr) && pr.GetString() is string s)
                        out_.Add(s);
        }
        catch { }
        return out_;
    }

    // Đọc web-vitals iife kèm trong assembly (EmbeddedResource).
    public static string LoadWebVitalsLib()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            string? name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("web-vitals.iife.js", StringComparison.OrdinalIgnoreCase));
            if (name is null) return "";
            using var s = asm.GetManifestResourceStream(name);
            if (s is null) return "";
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }
        catch { return ""; }
    }

    // Cầu JS: web-vitals -> chrome.webview.postMessage. Chạy 1 lần mọi document.
    public const string BridgePrefix = ";(function(){try{"
        + "if(!window.chrome||!window.chrome.webview||window.__cardonVitals)return;"
        + "window.__cardonVitals=1;"
        + "var send=function(m){try{window.chrome.webview.postMessage(JSON.stringify(m))}catch(e){}};"
        + "var rep=function(x){send({t:'vital',n:x.name,v:Math.round(x.value*100)/100,u:location.href})};"
        + "webVitals.onLCP(rep);webVitals.onCLS(rep);webVitals.onINP(rep);"
        + "}catch(e){}});";
}
