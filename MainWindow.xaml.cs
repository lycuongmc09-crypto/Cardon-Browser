using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using KmyBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace KmyBrowser;

public partial class MainWindow : Window
{
    private readonly HistoryService _history = new();
    private readonly SettingsService _settings = new();
    private readonly CprStore _cpr = new();
    private readonly PrismLedger _prism = new();
    private readonly SentryService _sentry = new();
    private readonly VitalsService _vitals = new();
    private string? _pendingPauserId;
    private bool _expectHistoryNav;
    private DateTime _lastBfcacheAt = DateTime.MinValue;
    private string? _lastBfcacheReasons;
    private string? _pendingPreloadId;
    private readonly ReuseGraph _reuse = new(50);
    private readonly ComputationCache<string, string> _resolveCache = new(200);
    private readonly ComputationCache<string, string> _pageCache = new(8);
    private readonly Stopwatch _navWatch = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += (_, _) => { try { _history.Flush(); } catch { } };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings.Load();
        _history.Load();
        _cpr.Load();
        _prism.Load();
        _vitals.Load();
        try
        {
            await Browser.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Không khởi tạo được WebView2. Hãy cài WebView2 Runtime.\n" + ex.Message,
                "Cardon Browser", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Back/Forward của Chromium đã dùng bfcache (khôi phục tức thì).
        // Ở đây lưu scroll trang cũ trước khi rời đi để fallback restore.
        Browser.NavigationStarting += (_, _) =>
        {
            LoadingBar.Visibility = Visibility.Visible;
            _navWatch.Restart();
            _ = SaveScrollBestEffortAsync();
        };
        Browser.NavigationCompleted += async (_, _) =>
        {
            LoadingBar.Visibility = Visibility.Collapsed;
            var url = Browser.Source?.ToString() ?? "";
            AddressBar.Text = url;
            if (!Navigator.IsInternal(url))
                _history.Add(Browser.CoreWebView2?.DocumentTitle ?? url, url);
            UpdateTitle();
            _navWatch.Stop();
            UpdateSentryTip();
            double navMs = _navWatch.Elapsed.TotalMilliseconds;
            await ReuseOrLearnAsync(url);
            await CprCollectAsync(url, navMs);
            await PrismMeasureAsync(url);
            await CdpMeasureAsync(url);
        };
        Browser.SourceChanged += (_, _) =>
        {
            var url = Browser.Source?.ToString() ?? "";
            AddressBar.Text = url;
        };

        // SENTRY Bước 1 — FILTER: 1 pipeline duy nhất, fail-open.
        try
        {
            Browser.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            Browser.CoreWebView2.WebResourceRequested += Sentry_OnRequest;
            Browser.CoreWebView2.WebResourceResponseReceived += Sentry_OnResponse;
        }
        catch { /* không hook được thì duyệt thường */ }

        // Quan trắc: CDP metrics + bfcache events + web-vitals bridge (1 lần).
        try
        {
            await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Performance.enable", "{}");
            await Browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.enable", "{}");
            var bfcacheRecv = Browser.CoreWebView2.GetDevToolsProtocolEventReceiver("Page.backForwardCacheNotUsed");
            bfcacheRecv.DevToolsProtocolEventReceived += (_, ev) =>
            {
                try
                {
                    _lastBfcacheReasons = string.Join(",", VitalsService.ParseBfcacheReasons(ev.ParameterObjectAsJson));
                    _lastBfcacheAt = DateTime.Now;
                }
                catch { }
            };
        }
        catch { }
        try
        {
            string lib = VitalsService.LoadWebVitalsLib();
            if (lib.Length > 0)
                await Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(lib + VitalsService.BridgePrefix);
            Browser.WebMessageReceived += (_, em) =>
            {
                try
                {
                    var parsed = VitalsService.ParseVitalMessage(em.TryGetWebMessageAsString());
                    if (parsed is { } p && p.url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        _vitals.NoteVital(p.url, p.name, p.value);
                        _vitals.Save();
                    }
                }
                catch { }
            };
        }
        catch { }

        NavigateHome();
    }

    private void UpdateTitle()
    {
        try
        {
            var t = Browser.CoreWebView2?.DocumentTitle;
            Title = string.IsNullOrWhiteSpace(t) ? "Cardon Browser" : $"{t} - Cardon Browser";
        }
        catch { }
    }

    private void NavigateHome()
    {
        var mode = _settings.Current.HomepageMode;
        if (mode == "Blank") NavigateTo(Navigator.BlankUri);
        else if (mode == "Custom" && !string.IsNullOrWhiteSpace(_settings.Current.CustomHomepageUrl))
            NavigateTo(_settings.Current.CustomHomepageUrl);
        else NavigateTo(Navigator.DashboardUri);
    }

    private async void NavigateTo(string url)
    {
        url = Navigator.NormalizeInternal(url);
        if (url == Navigator.DashboardUri)
        {
            // Reuse: HTML dashboard chỉ build 1 lần, lần sau lấy từ RAM.
            var html = _pageCache.GetOrAdd("dashboard", _ => Pages.Dashboard());
            Browser.NavigateToString(html);
            AddressBar.Text = Navigator.DashboardUri;
            return;
        }
        if (url == Navigator.AboutUri)
        {
            var html = _pageCache.GetOrAdd("about", _ => Pages.About());
            Browser.NavigateToString(html);
            AddressBar.Text = Navigator.AboutUri;
            return;
        }
        if (url == Navigator.CacheUri)
        {
            Browser.NavigateToString(Pages.CachePage(
                _resolveCache.HitRate, _resolveCache.Hits, _resolveCache.Misses,
                _reuse.FingerprintHits, _reuse.FingerprintMisses, _reuse.ScrollRestores,
                _reuse.Snapshot(), BuildCprSection() + BuildSentrySection() + BuildVitalsSection()));
            AddressBar.Text = Navigator.CacheUri;
            return;
        }
        // CPR replay: chèn preload của route đã học TRƯỚC khi navigate.
        _pendingPreloadId = null;
        _pendingPauserId = null;
        bool replayUsed = false;
        try
        {
            var hints = _cpr.GetPreloads(url);
            if (hints.Count > 0 && Browser.CoreWebView2 != null)
            {
                _pendingPreloadId = await Browser.CoreWebView2
                    .AddScriptToExecuteOnDocumentCreatedAsync(CprJs.PreloadInjector(hints));
                replayUsed = true;
            }
        }
        catch { _pendingPreloadId = null; replayUsed = false; }
        // GPU layer 2: pauser cho video muted ngoài viewport (trừ site allowlisted).
        try
        {
            if (Browser.CoreWebView2 != null
                && MediaPauser.ShouldArm(url, _sentry.IsSiteAllowed(SentryService.HostOf(url))))
                _pendingPauserId = await Browser.CoreWebView2
                    .AddScriptToExecuteOnDocumentCreatedAsync(MediaPauser.Script);
        }
        catch { _pendingPauserId = null; }
        try { Browser.CoreWebView2?.Navigate(url); }
        catch { Browser.Source = new Uri(url); }
        _replayArmed = replayUsed;
    }

    private bool _replayArmed;

    // CPR học: sau load ~2s thu performance entries, cập nhật EWMA + timing, gỡ script replay.
    private async Task CprCollectAsync(string url, double navMs)
    {
        bool used = _replayArmed;
        _replayArmed = false;
        // Chụp id vào biến cục bộ TRƯỚC delay: navigation mới đè id mới, không gỡ nhầm script của nó.
        string? preloadId = _pendingPreloadId;
        _pendingPreloadId = null;
        string? pauserId = _pendingPauserId;
        _pendingPauserId = null;
        try
        {
            if (preloadId != null && Browser.CoreWebView2 != null)
                Browser.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(preloadId);
            if (pauserId != null && Browser.CoreWebView2 != null)
                Browser.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(pauserId);
        }
        catch { }
        if (Navigator.IsInternal(url)) return;
        try
        {
            await Task.Delay(2000);
            // User đã navigate đi nơi khác trong lúc chờ => bỏ, tránh học nhầm trang.
            if (!Navigator.SamePage(Browser.Source?.ToString() ?? "", url)) return;
            var t = Browser.ExecuteScriptAsync(CprJs.CollectJs);
            if (await Task.WhenAny(t, Task.Delay(1500)) != t) return;
            var (rtt, entries) = CprJs.ParseCollect(await t);
            if (entries.Count == 0) return;
            _cpr.Learn(url, entries, rtt);
            _cpr.NoteTiming(url, navMs, used);
            _cpr.Save();
        }
        catch { }
    }

    private async Task<string?> CdpCallAsync(string method, string pars, int timeoutMs = 1500)
    {
        try
        {
            if (Browser.CoreWebView2 == null) return null;
            var t = Browser.CoreWebView2.CallDevToolsProtocolMethodAsync(method, pars);
            if (await Task.WhenAny(t, Task.Delay(timeoutMs)) != t) return null;
            return await t;
        }
        catch { return null; }
    }

    // Đo CDP sau load: nodes/jsHeap/layout + bfcache hit/miss cho history-nav.
    // Heuristic bfcache: event chỉ bắn khi MISS; history-nav xong mà không event gần đó => HIT.
    private async Task CdpMeasureAsync(string url)
    {
        if (Navigator.IsInternal(url) || Browser.CoreWebView2 == null) return;
        try
        {
            string? m = await CdpCallAsync("Performance.getMetrics", "{}");
            double layout = -1, jsHeap = 0;
            if (m != null)
            {
                var d = VitalsService.ParseMetrics(m);
                d.TryGetValue("LayoutCount", out layout);
                d.TryGetValue("JSHeapUsedSize", out jsHeap);
            }
            string? dc = await CdpCallAsync("Memory.getDOMCounters", "{}");
            int nodes = 0;
            if (dc != null) (nodes, _) = VitalsService.ParseDomCounters(dc);
            _vitals.NoteCdp(url, nodes, (long)jsHeap, layout);
            if (_expectHistoryNav)
            {
                _expectHistoryNav = false;
                bool miss = _lastBfcacheReasons != null
                    && (DateTime.Now - _lastBfcacheAt).TotalSeconds < 5;
                _vitals.NoteBfcache(url, !miss, miss ? _lastBfcacheReasons ?? "" : "");
            }
            _vitals.Save();
        }
        catch { }
    }
    private string BuildCprSection()
    {
        var sb = new System.Text.StringBuilder(
            "<h2 style=\"margin:18px 0 8px;font-size:16px\">Critical Path Replay</h2>"
            + "<table><tr><th>Route</th><th>Loads</th><th>Res</th><th>Baseline</th><th>Saved</th></tr>");
        foreach (var (route, d) in _cpr.Snapshot())
        {
            string r = route.Length > 45 ? route.Substring(0, 45) + "..." : route;
            sb.Append("<tr><td>").Append(System.Net.WebUtility.HtmlEncode(r)).Append("</td><td>")
              .Append(d.Loads).Append("</td><td>").Append(d.Resources.Count).Append("</td><td>")
              .Append(d.BaselineMs.ToString("F0")).Append("ms</td><td>")
              .Append(d.SavedMs.ToString("F0")).Append("ms</td></tr>");
        }
        sb.Append("</table>");
        if (_cpr.RouteCount == 0)
            sb.Append("<div style=\"opacity:.6;font-size:12.5px\">Chua hoc route nao - duyet vai trang 2-3 lan de CPR hoc chuoi.</div>");
        // PRISM P1: sổ cái hình học (measure-only).
        sb.Append("<h2 style=\"margin:18px 0 8px;font-size:16px\">PRISM Geometry Ledger (P1: chi do, chua cat trang)</h2>"
            + "<table><tr><th>Route</th><th>Visits</th><th>Segments</th></tr>");
        foreach (var (proute, pdata) in _prism.Snapshot())
        {
            string pr = proute.Length > 45 ? proute.Substring(0, 45) + "..." : proute;
            sb.Append("<tr><td>").Append(System.Net.WebUtility.HtmlEncode(pr)).Append("</td><td>")
              .Append(pdata.Visits).Append("</td><td>").Append(pdata.Segments.Count).Append("</td></tr>");
        }
        sb.Append("</table>");
        return sb.ToString();
    }

    // PRISM P1: đo hình học block sau load, học height EWMA. Không sửa trang.
    private async Task PrismMeasureAsync(string url)
    {
        if (Navigator.IsInternal(url)) return;
        try
        {
            var t = Browser.ExecuteScriptAsync(PrismMeasureJs.CollectJs);
            if (await Task.WhenAny(t, Task.Delay(1500)) != t) return;
            var (_, blocks) = PrismMeasureJs.Parse(await t);
            if (blocks.Count == 0) return;
            _prism.Learn(CprStore.NormalizeRoute(url), blocks);
            _prism.Save();
        }
        catch { }
    }

    // SENTRY: verdict sync <0.2ms. Bất kỳ exception nào => không set Response (passthrough).
    private void Sentry_OnRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            string topHost = SentryService.HostOf(Browser.Source?.ToString() ?? "");
            var v = _sentry.Decide(e.Request.Uri, topHost, out _);
            if (v == SentryService.Verdict.Block && Browser.CoreWebView2 != null)
                e.Response = Browser.CoreWebView2.Environment.CreateWebResourceResponse(
                    null, 204, "Blocked by SENTRY", "Content-Type: text/plain\r\n");
        }
        catch { }
    }

    // Học bytes thật từng host (Content-Length) để ước tính saved-bytes khi block.
    private void Sentry_OnResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            string? len = null;
            try { len = e.Response.Headers.GetHeader("Content-Length"); } catch { }
            if (len != null && long.TryParse(len, out long b))
                _sentry.LearnCost(e.Request.Uri, b);
        }
        catch { }
    }

    private void UpdateSentryTip()
    {
        try
        {
            string topHost = SentryService.HostOf(Browser.Source?.ToString() ?? "");
            bool off = _sentry.IsSiteAllowed(topHost);
            Dispatcher.Invoke(() =>
            {
                SentryBtn.Content = _sentry.BlockedTotal > 0 ? $"S{_sentry.BlockedTotal}" : "S";
                SentryBtn.ToolTip = off
                    ? $"SENTRY tắt cho site này (bấm để bật). Đã chặn {_sentry.BlockedTotal} request."
                    : $"SENTRY đang chặn tracker (bấm để tắt cho site này). Đã chặn {_sentry.BlockedTotal} request.";
            });
        }
        catch { }
    }

    private void SentryBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string topHost = SentryService.HostOf(Browser.Source?.ToString() ?? "");
            if (topHost == "") return;
            bool off = _sentry.IsSiteAllowed(topHost);
            _sentry.SetSiteAllowed(topHost, !off);
            UpdateSentryTip();
        }
        catch { }
    }

    private string BuildSentrySection()
    {
        double mb = _sentry.SavedBytesEst / (1024.0 * 1024);
        var sb = new System.Text.StringBuilder(
            "<h2 style=\"margin:18px 0 8px;font-size:16px\">SENTRY Network Triage (Buoc 1: FILTER)</h2>"
            + $"<div>Đã chặn <b>{_sentry.BlockedTotal}</b> request tracker/ads, ước tính tiết kiệm <b>{mb:F1}MB</b>. "
            + "First-party không bao giờ bị chặn. Tắt theo site bằng nút S trên thanh bar.</div>"
            + "<table><tr><th>Tracker domain</th><th>Bị chặn</th></tr>");
        foreach (var (host, c) in _sentry.TopBlocked())
            sb.Append("<tr><td><code>").Append(System.Net.WebUtility.HtmlEncode(host))
              .Append("</code></td><td>").Append(c).Append("</td></tr>");
        sb.Append("</table>");
        return sb.ToString();
    }

    // Reuse: input -> URL cũng cache (key gồm engine để đổi engine không sai).
    private void GoFromBar()
    {
        var input = (AddressBar.Text ?? "").Trim();
        var key = _settings.Current.SearchEngineName + "|" + input;
        var url = _resolveCache.GetOrAdd(key, _ => Navigator.ResolveInputToUrl(input, _settings));
        NavigateTo(url);
    }

    // Computational Reuse Graph ở tầng host:
    // fingerprint khớp => bỏ qua re-inject, chỉ restore scroll (tức thì).
    // fingerprint mới => học 1 lần rồi cache.
    private async Task ReuseOrLearnAsync(string url)
    {
        if (Navigator.IsInternal(url)) return;
        try
        {
            var snapTask = Browser.ExecuteScriptAsync(ReuseGraph.SnapshotJs);
            var done = await Task.WhenAny(snapTask, Task.Delay(1500));
            if (done != snapTask) return;
            var (fp, x, y, title) = ReuseGraph.ParseSnapshotJson(await snapTask);
            if (fp is "err" or "") return;

            if (_reuse.Matches(url, fp))
            {
                // 95% DOM giống nhau => không tính lại: chỉ restore scroll.
                if (_reuse.TryGet(url, out var cached) && cached is not null &&
                    (cached.ScrollX != 0 || cached.ScrollY != 0))
                {
                    await Browser.ExecuteScriptAsync(ReuseGraph.ScrollToJs(cached.ScrollX, cached.ScrollY));
                    _reuse.NoteScrollRestore();
                }
                return;
            }
            _reuse.Store(url, fp, x, y,
                string.IsNullOrWhiteSpace(title) ? url : title);
        }
        catch { /* reuse là tối ưu phụ — không bao giờ được làm hỏng navigation */ }
    }

    private async Task SaveScrollBestEffortAsync()
    {
        try
        {
            var oldUrl = Browser.Source?.ToString() ?? "";
            if (Navigator.IsInternal(oldUrl)) return;
            var t = Browser.ExecuteScriptAsync(ReuseGraph.ReadScrollJs);
            if (await Task.WhenAny(t, Task.Delay(600)) != t) return;
            var json = await t;
            // Đọc scroll đơn giản: {"x":..,"y":..} hoặc bọc chuỗi JSON.
            int sx = 0, sy = 0;
            try
            {
                var s = json.Trim().Trim('"').Replace("\\\"", "\"");
                using var doc = System.Text.Json.JsonDocument.Parse(s.StartsWith("{") ? s : "{\"x\":0,\"y\":0}");
                var r = doc.RootElement;
                if (r.TryGetProperty("x", out var px)) sx = px.GetInt32();
                if (r.TryGetProperty("y", out var py)) sy = py.GetInt32();
            }
            catch { return; }
            if (_reuse.TryGet(oldUrl, out var p) && p is not null)
                _reuse.Store(oldUrl, p.Fingerprint, sx, sy, p.Title);
        }
        catch { }
    }

    private string BuildVitalsSection()
    {
        var sb = new System.Text.StringBuilder(
            "<h2 style=\"margin:18px 0 8px;font-size:16px\">Web Vitals + CDP (do that trong page)</h2>"
            + "<div style=\"font-size:12px;opacity:.7\">LCP/CLS/INP tu web-vitals.js; nodes/jsHeap/layout tu CDP. "
            + "bfcache: event chi ban khi MISS — history-nav khong event gan do thi tinh HIT (heuristic).</div>"
            + "<table><tr><th>Route</th><th>LCP</th><th>CLS</th><th>INP</th><th>Nodes</th><th>JSHeap</th><th>bfcache h/m</th></tr>");
        foreach (var (route, v) in _vitals.Snapshot())
        {
            string r = route.Length > 40 ? route.Substring(0, 40) + "..." : route;
            string inp = v.InpMs <= 0 ? "-" : v.InpMs.ToString("F0") + "ms";
            sb.Append("<tr><td>").Append(System.Net.WebUtility.HtmlEncode(r)).Append("</td><td>")
              .Append(v.LcpMs > 0 ? v.LcpMs.ToString("F0") + "ms" : "-").Append("</td><td>")
              .Append(v.Cls > 0 ? v.Cls.ToString("F3") : "-").Append("</td><td>")
              .Append(inp).Append("</td><td>").Append(v.Nodes).Append("</td><td>")
              .Append(v.JsHeapBytes > 0 ? (v.JsHeapBytes / 1048576.0).ToString("F1") + "MB" : "-")
              .Append("</td><td>").Append(v.BfcacheHits).Append("/").Append(v.BfcacheMiss)
              .Append(string.IsNullOrEmpty(v.LastBfcacheReasons) ? "" : " (" + System.Net.WebUtility.HtmlEncode(v.LastBfcacheReasons) + ")")
              .Append("</td></tr>");
        }
        sb.Append("</table>");
        return sb.ToString();
    }

    private void GoBtn_Click(object sender, RoutedEventArgs e) => GoFromBar();
    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) GoFromBar();
    }
    private void BackBtn_Click(object sender, RoutedEventArgs e)
    { try { if (Browser.CanGoBack) { _expectHistoryNav = true; Browser.GoBack(); } } catch { } }
    private void FwdBtn_Click(object sender, RoutedEventArgs e)
    { try { if (Browser.CanGoForward) { _expectHistoryNav = true; Browser.GoForward(); } } catch { } }
    private void ReloadBtn_Click(object sender, RoutedEventArgs e)
    { try { Browser.Reload(); } catch { } }
    private void HomeBtn_Click(object sender, RoutedEventArgs e) => NavigateHome();
    private void AboutBtn_Click(object sender, RoutedEventArgs e) => NavigateTo(Navigator.AboutUri);

    private void HistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        var w = new HistoryWindow(_history);
        w.UrlChosen += url => NavigateTo(url);
        w.Owner = this;
        w.ShowDialog();
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var w = new SettingsWindow(_settings, _history);
        w.Owner = this;
        w.ShowDialog();
    }
}
