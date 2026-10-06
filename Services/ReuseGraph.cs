using System.Text.Json;

namespace KmyBrowser.Services;

// ReuseGraph: fingerprint cây con DOM + cache quyết định ở tầng host.
// - Fingerprint: hash cấu trúc nhẹ (tag + số con + độ dài text, tối đa 300 node),
//   KHÔNG serialize toàn bộ HTML nên rẻ (~1 ExecuteScriptAsync).
// - Khi fingerprint khớp: bỏ qua re-inject CSS/JS, chỉ restore scroll => gần như tức thì.
// - Back/Forward: Chromium bfcache đã khôi phục tức thì; cache này là fallback + đo lường.
public sealed class ReuseGraph
{
    public sealed record PageReuse(string Url, string Fingerprint, int ScrollX, int ScrollY, string Title, DateTime SeenAt);

    private readonly int _capacity;
    private readonly Dictionary<string, PageReuse> _byUrl = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new();
    private readonly HashSet<string> _styledFingerprints = new();
    private readonly object _lock = new();

    public int FingerprintHits { get; private set; }
    public int FingerprintMisses { get; private set; }
    public int ScrollRestores { get; private set; }

    public ReuseGraph(int capacity = 50)
    {
        _capacity = Math.Max(1, capacity);
    }

    // JS chạy trong page: duyệt tối đa 300 element, hash FNV-1a 32-bit.
    // Trả về JSON: {"fp":"...","x":scrollX,"y":scrollY,"t":title}
    public const string SnapshotJs = """
(() => {
  try {
    const cap = 300;
    const els = document.getElementsByTagName('*');
    const n = Math.min(els.length, cap);
    let h = 0x811c9dc5;
    const mix = (s) => { for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193) >>> 0; } };
    for (let i = 0; i < n; i++) {
      const el = els[i];
      mix(el.tagName + '|' + el.childElementCount + '|' + ((el.innerText || '').length) + ';');
    }
    mix('|N=' + els.length);
    return JSON.stringify({ fp: (h >>> 0).toString(16), x: Math.round(window.scrollX || 0), y: Math.round(window.scrollY || 0), t: (document.title || '').slice(0, 120) });
  } catch (e) { return JSON.stringify({ fp: 'err', x: 0, y: 0, t: '' }); }
})();
""";

    public const string ReadScrollJs = """
JSON.stringify({ x: Math.round(window.scrollX || 0), y: Math.round(window.scrollY || 0) })
""";

    public static string ScrollToJs(int x, int y) => $"window.scrollTo({x}, {y});";

    public bool TryGet(string url, out PageReuse? page)
    {
        lock (_lock) return _byUrl.TryGetValue(NormUrl(url), out page);
    }

    // Trả về true nếu fingerprint khớp cache => caller được phép BỎ QUA re-style/re-inject.
    public bool Matches(string url, string fingerprint)
    {
        lock (_lock)
        {
            if (_byUrl.TryGetValue(NormUrl(url), out var p) && p.Fingerprint == fingerprint)
            {
                FingerprintHits++;
                return true;
            }
            FingerprintMisses++;
            return false;
        }
    }

    public void Store(string url, string fingerprint, int x, int y, string title)
    {
        if (string.IsNullOrWhiteSpace(url) || fingerprint is not { Length: > 0 }) return;
        lock (_lock)
        {
            var key = NormUrl(url);
            _byUrl[key] = new PageReuse(key, fingerprint, x, y, title, DateTime.Now);
            _lru.Remove(key);
            _lru.AddFirst(key);
            while (_byUrl.Count > _capacity && _lru.Last is not null)
            {
                _byUrl.Remove(_lru.Last.Value);
                _lru.RemoveLast();
            }
        }
    }

    // Đánh dấu fingerprint đã được style/inject => lần sau khớp thì skip.
    public bool ShouldSkipStyling(string fingerprint)
    {
        lock (_lock)
        {
            if (_styledFingerprints.Contains(fingerprint)) return true;
            _styledFingerprints.Add(fingerprint);
            if (_styledFingerprints.Count > 500) _styledFingerprints.Clear();
            return false;
        }
    }

    public void NoteScrollRestore()
    {
        lock (_lock) ScrollRestores++;
    }

    public IReadOnlyList<PageReuse> Snapshot()
    {
        lock (_lock) return _byUrl.Values.OrderByDescending(p => p.SeenAt).Take(50).ToList();
    }

    public static (string fp, int x, int y, string title) ParseSnapshotJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            // ExecuteScriptAsync bọc kết quả JSON trong chuỗi JSON => unwrap 1 lớp nếu cần.
            if (r.ValueKind == JsonValueKind.String)
            {
                var inner = r.GetString() ?? "{}";
                using var doc2 = JsonDocument.Parse(inner);
                r = doc2.RootElement.Clone();
            }
            string fp = r.TryGetProperty("fp", out var f) ? f.GetString() ?? "err" : "err";
            int x = r.TryGetProperty("x", out var px) ? px.GetInt32() : 0;
            int y = r.TryGetProperty("y", out var py) ? py.GetInt32() : 0;
            string t = r.TryGetProperty("t", out var pt) ? pt.GetString() ?? "" : "";
            return (fp, x, y, t);
        }
        catch { return ("err", 0, 0, ""); }
    }

    private static string NormUrl(string url)
    {
        url = (url ?? "").Trim();
        int h = url.IndexOf('#');
        if (h >= 0) url = url[..h];
        return url.TrimEnd('/');
    }
}
