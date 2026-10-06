namespace KmyBrowser.Services;

// Cardon Browser — bản sắc riêng: carbon + amber, tagline "Sharp. Swift. Yours."
// CUSTOMIZE: sửa HTML tại đây.
public static class Pages
{
    public static string Dashboard() => """
<!DOCTYPE html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Cardon Browser - Home</title>
<style>
*{box-sizing:border-box;margin:0;padding:0;font-family:'Segoe UI',Arial,sans-serif}
body{min-height:100vh;background:radial-gradient(1000px 500px at 20% 10%,#3b2f0b 0%,transparent 60%),radial-gradient(900px 500px at 90% 90%,#0e3a45 0%,transparent 55%),linear-gradient(135deg,#0b0d12,#141a24 55%,#0b0d12);color:#f3f4f6;display:flex;align-items:center;justify-content:center;padding:32px}
.card{max-width:760px;width:100%;background:rgba(17,20,27,.72);border:1px solid rgba(245,158,11,.28);border-radius:22px;padding:42px 38px;box-shadow:0 24px 70px rgba(0,0,0,.5),inset 0 1px 0 rgba(255,255,255,.06)}
.brand{display:flex;align-items:center;gap:12px;margin-bottom:18px}
.mark{width:40px;height:40px;border-radius:12px;background:linear-gradient(135deg,#f59e0b,#fbbf24);display:flex;align-items:center;justify-content:center;font-weight:900;font-size:22px;color:#111827;box-shadow:0 6px 20px rgba(245,158,11,.45)}
.brand b{letter-spacing:4px;font-size:15px;color:#fbbf24}
.tag{font-size:11px;letter-spacing:2px;color:#9ca3af;border:1px solid #374151;border-radius:999px;padding:5px 12px;margin-left:auto}
h1{font-size:40px;line-height:1.1;margin:6px 0 10px}
h1 span{background:linear-gradient(90deg,#fbbf24,#f59e0b 40%,#22d3ee);-webkit-background-clip:text;background-clip:text;color:transparent}
p.sub{opacity:.8;margin-bottom:22px;font-size:15px}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:12px;margin:22px 0}
.tile{background:rgba(255,255,255,.05);border:1px solid rgba(255,255,255,.1);border-radius:14px;padding:16px;font-size:13.5px;line-height:1.45}
.tile b{display:block;margin-bottom:6px;font-size:12px;letter-spacing:1px;text-transform:uppercase;color:#fbbf24}
.quick{display:flex;flex-wrap:wrap;gap:8px;margin:4px 0 18px}
.quick a{font-size:13px;color:#e5e7eb;text-decoration:none;background:rgba(255,255,255,.06);border:1px solid rgba(255,255,255,.12);padding:8px 14px;border-radius:999px}
.quick a:hover{border-color:#f59e0b;color:#fbbf24}
.hint{font-size:12.5px;opacity:.7;background:rgba(0,0,0,.35);border:1px solid rgba(255,255,255,.08);border-radius:10px;padding:12px 14px}
code{background:rgba(245,158,11,.15);color:#fcd34d;padding:2px 8px;border-radius:6px;font-size:12px}
</style></head><body>
<div class="card">
<div class="brand"><div class="mark">C</div><b>CARDON</b><div class="tag">SHARP &bull; SWIFT &bull; YOURS</div></div>
<h1>Cut through the noise.<br><span>Browse Cardon.</span></h1>
<p class="sub">Trình duyệt nhẹ WebView2, tùy biến sâu: lịch sử lưu file, settings linh hoạt, không rườm rà.</p>
<div class="quick">
<a href="https://www.google.com">Google</a>
<a href="https://www.youtube.com">YouTube</a>
<a href="https://github.com">GitHub</a>
<a href="https://stackoverflow.com">StackOverflow</a>
</div>
<div class="grid">
<div class="tile"><b>Smart bar</b>Gõ URL hoặc từ khóa — Cardon tự phân biệt và tìm đúng chỗ.</div>
<div class="tile"><b>History file</b>Lưu JSON bền vững, tìm kiếm nhanh, mở lại 1 click đúp.</div>
<div class="tile"><b>Settings</b>Đổi engine tìm kiếm + trang chủ theo gu của bạn.</div>
</div>
<div class="hint">Trang nội bộ: <code>cardon://dashboard</code> &nbsp; <code>cardon://about</code> &nbsp; <code>about:blank</code></div>
</div></body></html>
""";

    public static string About() => """
<!DOCTYPE html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>About Cardon Browser</title>
<style>body{font-family:'Segoe UI',Arial;background:#0b0d12;color:#e5e7eb;display:flex;justify-content:center;padding:40px}
.card{max-width:640px;background:#141a24;border:1px solid rgba(245,158,11,.3);border-radius:18px;padding:34px}
.mark{width:44px;height:44px;border-radius:12px;background:linear-gradient(135deg,#f59e0b,#fbbf24);display:flex;align-items:center;justify-content:center;font-weight:900;font-size:24px;color:#111827;margin-bottom:14px}
h1{margin-bottom:8px;font-size:26px}a{color:#fbbf24}.v{color:#9ca3af;font-size:13px}</style></head><body>
<div class="card"><div class="mark">C</div>
<h1>Cardon Browser</h1><div class="v">v1.0.0 &bull; Sharp. Swift. Yours.</div>
<br><p>Lightweight <b>WebView2 (Chromium)</b> browser built with <b>C# WPF</b> for deep customization.</p>
<br><p>Features: smart address bar, file-based history, search-engine + homepage settings, clean card UI.</p>
<br><p>Inspired by KMY-Browser concept, rebuilt independently as Cardon with its own identity.</p>
</div></body></html>
""";
}
