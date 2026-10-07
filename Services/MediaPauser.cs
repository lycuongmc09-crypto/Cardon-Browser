namespace KmyBrowser.Services;

// GPU layer 2 — MediaPauser: tắt video muted ngoài viewport (previews/ads tự chạy).
// Chạy mọi frame (top + iframe) qua AddScriptToExecuteOnDocumentCreated.
// KHÔNG pause: video có tiếng (cần gesture mới chạy được nên là ý định user),
// video đang trong viewport (user nhìn thấy). Site allowlisted => không arm.
public static class MediaPauser
{
    public static bool ShouldArm(string url, bool siteAllowed)
    {
        if (siteAllowed) return false;
        return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    // Chạy TRƯỚC parse nên phải chờ DOM: MutationObserver + load + scroll.
    public const string Script = """
(() => {
  try {
    if (location.protocol !== 'http:' && location.protocol !== 'https:') return;
    if (window.__cardonPauser) return;
    window.__cardonPauser = 1;
    let queued = false;
    const inView = (el) => {
      try {
        const r = el.getBoundingClientRect();
        return r.bottom > 0 && r.top < window.innerHeight && r.right > 0 && r.left < window.innerWidth;
      } catch (e) { return true; }
    };
    const sweep = () => {
      queued = false;
      try {
        const vs = document.getElementsByTagName('video');
        for (let i = 0; i < vs.length; i++) {
          const v = vs[i];
          try {
            // muted + đang chạy + ngoài màn hình => preview/ad => pause.
            // Có tiếng hoặc trong viewport: không động vào.
            if (!v.paused && v.muted && !inView(v)) v.pause();
          } catch (e) {}
        }
      } catch (e) {}
    };
    const kick = () => { if (!queued) { queued = true; requestAnimationFrame(sweep); } };
    const boot = () => {
      try {
        sweep();
        new MutationObserver(kick).observe(document.documentElement, { childList: true, subtree: true });
        addEventListener('scroll', kick, { passive: true });
        addEventListener('load', sweep);
      } catch (e) {}
    };
    if (document.readyState === 'loading') addEventListener('DOMContentLoaded', boot, { once: true });
    else boot();
  } catch (e) {}
})();
""";
}
