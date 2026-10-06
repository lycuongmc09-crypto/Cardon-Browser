using System.Text.Json;

namespace KmyBrowser.Services;

// PRISM P1 — JS đo hình học các block top-level (1 lần sau load, ~200 block).
// Không sửa DOM, không chèn gì thường trực: zero rủi ro.
public static class PrismMeasureJs
{
    public const string CollectJs = """
(() => {
  try {
    const roots = [];
    const cands = ['main', 'article', '#mw-content-text .mw-parser-output', '.mw-parser-output', '#mw-content-text', '#content', '[role=main]', 'body'];
    let scope = document.body, bestN = -1;
    for (const sel of cands) {
      const el = document.querySelector(sel);
      if (!el) continue;
      const n = el.children.length;
      if (n > bestN) { bestN = n; scope = el; }
    }
    const kids = Array.from(scope.children).slice(0, 200);
    for (const el of kids) {
      if (!(el instanceof HTMLElement)) continue;
      const r = el.getBoundingClientRect();
      const htmlLen = (el.innerHTML || '').length;
      const k = el.querySelectorAll('*').length;
      roots.push({
        tag: el.tagName.toLowerCase(),
        cls: (el.className && el.className.baseVal !== undefined) ? '' : String(el.className || '').slice(0, 120),
        kids: k, htmlLen: htmlLen,
        top: Math.round(r.top + window.scrollY), h: Math.round(r.height),
        scr: el.getElementsByTagName('script').length > 0 ? 1 : 0,
        frm: el.tagName === 'FORM' || el.getElementsByTagName('form').length > 0 ? 1 : 0
      });
    }
    return JSON.stringify({ n: document.getElementsByTagName('*').length, blocks: roots });
  } catch (e) { return JSON.stringify({ n: 0, blocks: [] }); }
})();
""";

    public static (int nodes, List<PrismLedger.BlockDesc> blocks) Parse(string json)
    {
        var list = new List<PrismLedger.BlockDesc>();
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
            int n = root.TryGetProperty("n", out var pn) ? pn.GetInt32() : 0;
            if (root.TryGetProperty("blocks", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var b in arr.EnumerateArray())
                {
                    string tag = b.TryGetProperty("tag", out var t) ? t.GetString() ?? "div" : "div";
                    string cls = b.TryGetProperty("cls", out var c) ? c.GetString() ?? "" : "";
                    int kids = b.TryGetProperty("kids", out var k) ? k.GetInt32() : 0;
                    int len = b.TryGetProperty("htmlLen", out var l) ? l.GetInt32() : 0;
                    double top = b.TryGetProperty("top", out var tp) ? tp.GetDouble() : 0;
                    double h = b.TryGetProperty("h", out var hh) ? hh.GetDouble() : 0;
                    bool scr = b.TryGetProperty("scr", out var s) && s.GetInt32() == 1;
                    bool frm = b.TryGetProperty("frm", out var f) && f.GetInt32() == 1;
                    list.Add(new PrismLedger.BlockDesc(tag, cls, kids, len, top, h, scr, frm));
                }
            return (n, list);
        }
        catch { return (0, list); }
    }
}
