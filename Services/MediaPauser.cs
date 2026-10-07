namespace KmyBrowser.Services;

// GPU layer 2 — MediaPauser: tắt video muted ngoài viewport (previews/ads tự chạy).
// + LazyImages: ảnh/iframe dưới sâu tự loading=lazy (kiểu Opera Turbo phía client).
// Chạy mọi frame (top + iframe) qua AddScriptToExecuteOnDocumentCreated.
// KHÔNG pause: video có tiếng (cần gesture nên là ý định user),
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

    // Ảnh/iframe dưới sâu (>2 viewport): loading=lazy + decoding=async.
    // Trung thực: MutationObserver bắt kịp một phần (preload scanner có thể đi trước),
    // nhưng miễn phí và không vỡ gì — phần nặng đã có SENTRY lo.
    public const string LazyImages = """
(() => {
  try {
    if (location.protocol !== 'http:' && location.protocol !== 'https:') return;
    if (window.__cardonLazy) return;
    window.__cardonLazy = 1;
    const sweep = () => {
      try {
        const lim = window.innerHeight * 2;
        const below = (el) => {
          try { return el.getBoundingClientRect().top > lim; } catch (e) { return false; }
        };
        const imgs = document.getElementsByTagName('img');
        for (let i = 0; i < imgs.length; i++) {
          const im = imgs[i];
          try {
            if (!im.hasAttribute('loading') && below(im)) im.loading = 'lazy';
            if (!im.hasAttribute('decoding')) im.decoding = 'async';
          } catch (e) {}
        }
        const frs = document.getElementsByTagName('iframe');
        for (let i = 0; i < frs.length; i++) {
          const f = frs[i];
          try { if (!f.hasAttribute('loading') && below(f)) f.loading = 'lazy'; } catch (e) {}
        }
      } catch (e) {}
    };
    let queued = false;
    const kick = () => { if (!queued) { queued = true; requestAnimationFrame(sweep); } };
    if (document.readyState === 'loading') addEventListener('DOMContentLoaded', () => {
      sweep();
      try { new MutationObserver(kick).observe(document.documentElement, { childList: true, subtree: true }); } catch (e) {}
    }, { once: true });
    else sweep();
  } catch (e) {}
})();
""";
}
