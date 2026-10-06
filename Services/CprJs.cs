using System.Text;
using System.Text.Json;

namespace KmyBrowser.Services;

// JS chạy trong page cho CPR P1.
public static class CprJs
{
    // Thu thập resource sau load: tối đa 120 entry cuối, field gọn nhẹ.
    public const string CollectJs = """
(() => {
  try {
    const es = performance.getEntriesByType('resource').slice(-300);
    const nav = performance.getEntriesByType('navigation')[0];
    const rtt = nav ? Math.round((nav.responseStart - nav.requestStart) + (nav.connectEnd - nav.connectStart)) : 0;
    const out = es.map(e => ({ u: (e.name || '').slice(0, 300), i: e.initiatorType || '', s: Math.round(e.transferSize || e.encodedBodySize || 0) }));
    return JSON.stringify({ rtt, res: out });
  } catch (e) { return JSON.stringify({ rtt: 0, res: [] }); }
})();
""";

    // Sinh script chèn <link> preload: chạy trước parse (qua AddScriptToExecuteOnDocumentCreated).
    public static string PreloadInjector(List<CprStore.Preload> preloads)
    {
        var sb = new StringBuilder("(()=>{try{var H=document.head||document.documentElement;var L=[");
        bool first = true;
        foreach (var p in preloads)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("['").Append(p.Url.Replace("'", "%27")).Append("','")
              .Append(p.Rel).Append("','").Append(p.As).Append("',")
              .Append(p.CrossOrigin ? "1" : "0").Append(']');
        }
        sb.Append("];for(var k=0;k<L.length;k++){var l=document.createElement('link');"
            + "l.rel=L[k][1];l.href=L[k][0];if(L[k][2])l.as=L[k][2];"
            + "if(L[k][3])l.crossOrigin='anonymous';l.fetchPriority='high';H.appendChild(l);}"
            + "}catch(e){}})();");
        return sb.ToString();
    }

    // Parse kết quả CollectJs (chịu được lớp bọc chuỗi của ExecuteScriptAsync).
    public static (double rtt, List<(string url, string initiator, int bytes)> entries) ParseCollect(string json)
    {
        var list = new List<(string, string, int)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                var inner = root.GetString() ?? "{}";
                using var doc2 = JsonDocument.Parse(inner);
                root = doc2.RootElement.Clone();
            }
            double rtt = root.TryGetProperty("rtt", out var r) ? r.GetDouble() : 0;
            if (root.TryGetProperty("res", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray())
                {
                    string u = e.TryGetProperty("u", out var pu) ? pu.GetString() ?? "" : "";
                    string i = e.TryGetProperty("i", out var pi) ? pi.GetString() ?? "" : "";
                    int s = e.TryGetProperty("s", out var ps) ? ps.GetInt32() : 0;
                    if (!string.IsNullOrWhiteSpace(u)) list.Add((u, i, Math.Max(0, s)));
                }
            return (rtt, list);
        }
        catch { return (0, list); }
    }
}
