using System.IO;
using System.Text.Json;

namespace KmyBrowser.Services;

// SENTRY Bước 1 — FILTER: pipeline verdict duy nhất cho mọi request.
// Quyết định sync <0.2ms (HashSet + so suffix), fail-open: exception => Allow.
// - Không bao giờ block first-party (so host, subdomain của trang tính là first-party).
// - Allowlist theo site (sentry.allow.json) + blocklist người dùng (sentry.block.txt).
// - Stats: blocked count + bytes ước tính qua CostLedger (học Content-Length thật).
// Persist: %AppData%/CardonBrowser/sentry.allow.json (+ sentry.block.txt do user tự sửa).
public sealed class SentryService
{
    public enum Verdict { Allow, Block }

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CardonBrowser");
    private static readonly string AllowPath = Path.Combine(DataDir, "sentry.allow.json");
    private static readonly string BlockPath = Path.Combine(DataDir, "sentry.block.txt");

    // Blocklist kèm sẵn: pure tracker/ads/beacon. KHÔNG gồm tag-manager/consent
    // (chặn loader dễ vỡ trang hơn là lợi) — user tự thêm vào sentry.block.txt nếu muốn.
    private static readonly string[] Builtin = [
        "google-analytics.com", "ssl.google-analytics.com", "analytics.google.com",
        "doubleclick.net", "googlesyndication.com", "googleadservices.com", "adservice.google.com",
        "facebook.net", "connect.facebook.net", "pixel.facebook.com",
        "hotjar.com", "static.hotjar.com", "fullstory.com", "mixpanel.com", "api.mixpanel.com",
        "segment.com", "cdn.segment.com", "amplitude.com", "api.amplitude.com",
        "newrelic.com", "nr-data.net", "taboola.com", "outbrain.com", "criteo.com",
        "quantserve.com", "scorecardresearch.com", "moatads.com", "moat.com",
        "ads.yahoo.com", "adsystem.com", "amazon-adsystem.com", "ads-twitter.com", "static.ads-twitter.com",
        "ads.linkedin.com", "px.ads.linkedin.com", "analytics.twitter.com",
        "bat.bing.com", "clarity.ms", "c.clarity.ms",
        "matomo.cloud", "piwik.pro", "counter.dev", "plausible.io",
        "sentry.io", "bugsnag.com", "logrocket.com",
        "intercom.io", "widget.intercom.io", "drift.com", "crisp.chat", "tawk.to",
        "admicro.vn", "ads.admicro.vn", "vcmedia.vn",
        "popads.net", "popcash.net", "adnxs.com", "rubiconproject.com", "pubmatic.com",
        "openx.net", "smartadserver.com", "adsrvr.org", "mathtag.com", "crwdcntrl.net",
        "demdex.net", "agkn.com", "eyeota.net", "exelator.com", "rfpcdn.com",
        "onesignal.com", "pushwoosh.com", "customer.io",
    ];

    private readonly HashSet<string> _blocked;
    private readonly HashSet<string> _allowedSites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (long total, int n)> _cost = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _blockedByHost = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private const long DefaultBytes = 24 * 1024;

    public int BlockedTotal { get; private set; }
    public long SavedBytesEst { get; private set; }

    public SentryService()
    {
        _blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in Builtin)
        {
            var t = d.Trim();
            if (t.Length > 0) _blocked.Add(t);
        }
        LoadUserFiles();
    }

    private void LoadUserFiles()
    {
        try
        {
            if (File.Exists(BlockPath))
                foreach (var line in File.ReadAllLines(BlockPath))
                {
                    var t = line.Trim().ToLowerInvariant();
                    if (t.Length > 0 && !t.StartsWith("#")) _blocked.Add(t);
                }
            if (File.Exists(AllowPath))
            {
                var arr = JsonSerializer.Deserialize<string[]>(File.ReadAllText(AllowPath));
                if (arr is not null)
                    foreach (var h in arr) _allowedSites.Add(h.Trim().ToLowerInvariant());
            }
        }
        catch { }
    }

    public static string HostOf(string url)
    {
        try { return new Uri(url).Host.ToLowerInvariant(); }
        catch { return ""; }
    }

    // First-party: cùng host hoặc subdomain của trang (heuristic, đủ cho shell).
    public static bool IsFirstParty(string reqHost, string topHost)
    {
        if (string.IsNullOrEmpty(reqHost) || string.IsNullOrEmpty(topHost)) return true;
        return reqHost == topHost || reqHost.EndsWith("." + topHost, StringComparison.Ordinal);
    }

    // Khớp blocklist theo suffix: x.tracker.com khớp tracker.com.
    public bool IsBlockedHost(string host)
    {
        if (_blocked.Contains(host)) return true;
        int dot = host.IndexOf('.');
        while (dot >= 0)
        {
            host = host[(dot + 1)..];
            if (_blocked.Contains(host)) return true;
            dot = host.IndexOf('.');
        }
        return false;
    }

    public bool IsSiteAllowed(string topHost)
    {
        lock (_lock) return _allowedSites.Contains(topHost);
    }

    public void SetSiteAllowed(string topHost, bool allowed)
    {
        lock (_lock)
        {
            if (allowed) _allowedSites.Add(topHost);
            else _allowedSites.Remove(topHost);
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(AllowPath, JsonSerializer.Serialize(
                    _allowedSites.ToArray(), new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }

    public Verdict Decide(string requestUrl, string topHost, out string reason)
    {
        reason = "";
        try
        {
            string host = HostOf(requestUrl);
            if (host == "" || IsFirstParty(host, topHost)) return Verdict.Allow;
            lock (_lock)
            {
                if (_allowedSites.Contains(topHost)) { reason = "allowlisted-site"; return Verdict.Allow; }
            }
            if (!IsBlockedHost(host)) return Verdict.Allow;
            reason = "tracker-list";
            lock (_lock)
            {
                BlockedTotal++;
                _blockedByHost.TryGetValue(host, out int c);
                _blockedByHost[host] = c + 1;
                long avg = AvgBytesLocked(host);
                SavedBytesEst += avg;
            }
            return Verdict.Block;
        }
        catch { return Verdict.Allow; } // fail-open
    }

    public void LearnCost(string requestUrl, long bytes)
    {
        try
        {
            string host = HostOf(requestUrl);
            if (host == "" || bytes <= 0 || bytes > 50_000_000) return;
            lock (_lock)
            {
                _cost.TryGetValue(host, out var cur);
                _cost[host] = (cur.total + bytes, cur.n + 1);
            }
        }
        catch { }
    }

    private long AvgBytesLocked(string host) =>
        _cost.TryGetValue(host, out var c) && c.n > 0 ? c.total / c.n : DefaultBytes;

    public List<(string host, int count)> TopBlocked(int n = 15)
    {
        lock (_lock)
            return _blockedByHost.OrderByDescending(kv => kv.Value)
                .Take(n).Select(kv => (kv.Key, kv.Value)).ToList();
    }
}
