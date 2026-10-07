namespace KmyBrowser.Services;

// Brave Shields (phần host làm được): strip query tracker + debounce redirect
// + HTTPS upgrade. Không đụng cookie/fingerprint (cần patch renderer — nói thẳng là bỏ).
public static class ShieldUrls
{
    // Tham số tracking: analytics/ads. KHÔNG gồm page/lang/tab/sort (xem CprStore.KeepQuery
    // — strip chỉ chạy trên navigation/document, CPR vẫn học route đã normalize riêng).
    private static readonly HashSet<string> TrackerParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id",
        "utm_source_platform", "utm_creative_format", "utm_marketing_tactic",
        "fbclid", "fb_action_ids", "fb_action_types", "fb_source",
        "gclid", "gbraid", "wbraid", "dclid",
        "msclkid", "mc_eid", "mc_cid",
        "yclid", "ymclid", "_openstat", "vero_id", "vero_conv",
        "mkt_tok", "pk_campaign", "pk_kwd", "piwik_campaign",
        "matomo_campaign", "matomo_kwd", "mtm_campaign", "mtm_kwd",
        "ga_source", "ga_medium", "ga_term", "ga_content", "ga_campaign",
        "srsltid", "sc_customer", "sc_channel", "ttclid", "twclid", "igshid",
        "ref", "ref_src", "ref_url", "spm", "scm", "share_medium",
    };

    // Bỏ param tracker, giữ nguyên thứ tự các param còn lại. Trả về url gốc nếu không đổi.
    public static string StripTrackingParams(string url)
    {
        try
        {
            var u = new Uri(url);
            if (string.IsNullOrEmpty(u.Query)) return url;
            var keep = new List<string>();
            bool dropped = false;
            foreach (var kv in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string k = kv.Split('=')[0];
                if (TrackerParams.Contains(Uri.UnescapeDataString(k))) dropped = true;
                else keep.Add(kv);
            }
            if (!dropped) return url;
            var b = new UriBuilder(u) { Query = string.Join("&", keep) };
            return b.Uri.AbsoluteUri;
        }
        catch { return url; }
    }

    // Debounce: redirector trung gian -> đích trực tiếp (tránh bounce-tracker set cookie).
    // Chỉ nhận URL đích http(s) hợp lệ, từ chối javascript:/data:.
    public static string? TryDebounce(string url)
    {
        try
        {
            var u = new Uri(url);
            string host = u.Host.ToLowerInvariant();
            var q = ParseQuery(u.Query);
            string? dest = null;
            if ((host == "www.google.com" || host == "google.com") && u.AbsolutePath == "/url")
                q.TryGetValue("q", out dest);
            else if (host == "l.facebook.com" && u.AbsolutePath == "/l.php")
                q.TryGetValue("u", out dest);
            else if ((host == "t.co" || host == "bit.ly" || host == "tinyurl.com" || host == "ow.ly")
                && !string.IsNullOrEmpty(u.AbsolutePath.Trim('/')))
                return url; // short-link: đích nằm ở server, không đoán — để nguyên
            if (dest != null
                && (dest.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || dest.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                return dest;
            return null;
        }
        catch { return null; }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = kv.IndexOf('=');
            string k = eq < 0 ? kv : kv[..eq];
            string v = eq < 0 ? "" : kv[(eq + 1)..];
            try { d[Uri.UnescapeDataString(k)] = Uri.UnescapeDataString(v); }
            catch { d[k] = v; }
        }
        return d;
    }

    // http:// thường -> https:// (trừ localhost/file). Fallback http do MainWindow xử lý khi lỗi.
    public static string? TryUpgradeHttps(string url)
    {
        try
        {
            var u = new Uri(url);
            if (u.Scheme != Uri.UriSchemeHttp) return null;
            if (u.Host is "localhost" or "127.0.0.1" or "[::1]") return null;
            var b = new UriBuilder(u) { Scheme = Uri.UriSchemeHttps, Port = -1 };
            string https = b.Uri.AbsoluteUri;
            return https == url ? null : https;
        }
        catch { return null; }
    }

    // Cắt Referer cross-origin còn origin (giữ same-origin nguyên — tránh vỡ hotlink/CSRF check).
    // Trả về null = giữ nguyên header.
    public static string? TrimReferer(string requestUrl, string topUrl)
    {
        try
        {
            var req = new Uri(requestUrl);
            var top = new Uri(topUrl);
            if (req.Host.Equals(top.Host, StringComparison.OrdinalIgnoreCase)) return null;
            return top.Scheme + "://" + top.Host + (top.IsDefaultPort ? "" : ":" + top.Port) + "/";
        }
        catch { return null; }
    }
}
