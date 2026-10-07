# Cardon — Thuật toán tăng tốc (ghi chú kỹ thuật, tiếng Việt)

## 0. Quan trắc (đo thật, không ước)
- `Services/VitalsService.cs`: LCP/CLS/INP thật từ web-vitals.js v4 (Apache-2.0, kèm trong
  assembly), nodes/jsHeap/layout từ CDP (`Performance.getMetrics`, `Memory.getDOMCounters`),
  bfcache hit/miss từ event `Page.backForwardCacheNotUsed` (method query không tồn tại trên
  Chromium thực tế — đã verify). Xem ở `cardon://cache`.
- RAM renderer thật (probe Chromium cùng engine, order-balanced n=2):
  vnexpress −28% (745→538MB), dantri −17% (516→427MB), tuoitre −2%, google 0% (trang sạch,
  đúng là không có gì để chặn). Đo cả browser process headless-shell nên số tuyệt đối cao;
  hướng và tỉ lệ mới là điều đáng đọc.

## 1. Reuse Graph — trí nhớ ngắn hạn
Cache kết quả tính toán trong RAM (`Services/ComputationCache.cs` — LRU):
- Resolve input→URL, HTML trang nội bộ (build 1 lần).
- `Services/ReuseGraph.cs` — fingerprint cấu trúc DOM (FNV-1a, ≤300 node).
  Khớp → bỏ re-inject, chỉ restore scroll. Back/forward dùng bfcache của Chromium.
- Đo ở `cardon://cache`. Test: hit ~93–97% workload trùng lặp.

## 2. CPR P1 — Critical Path Replay
Học chuỗi tài nguyên theo route (`Services/CprStore.cs` + `Services/CprJs.cs`, persist `cpr.json`):
- Sau load ~2s thu `performance.getEntriesByType('resource')`, chuẩn hóa route
  (template `:id`, bỏ query rác), HitRate kiểu EWMA.
- Từ lần 4+, chèn `<link rel=preload/modulepreload>` cho resource điểm cao
  (depth≥2, HitRate≥0.8, budget 150KB) qua `AddScriptToExecuteOnDocumentCreated`.
- Không preload beacon/log (denylist). Depth xấp xỉ từ `initiatorType` (CDP thật để P2).
- Live test: google.com tiết kiệm ~448ms (A/B đo thật). Xem `docs/reports/Cardon-CPR-Report.html`.

## 3. PRISM P1 — Geometry Ledger (mới chỉ ĐO, chưa cắt trang)
Học chiều cao từng khúc trang (`Services/PrismLedger.cs` + `Services/PrismMeasureJs.cs`,
persist `prism.json`). Fingerprint = tag + class + bucket text (block lặp tách bằng thứ tự).
- Live test (Chromium thật): Wikipedia WWII 17.009 nodes → 9/12 block eligible (75%),
  defer ~2,1MB HTML → RAM renderer −~14,8MB, height drift 0,0% (CLS≈0).
- Xem `docs/reports/Cardon-PRISM-Report.html`. P2 mới cắt HTML + Byte Pool.

## 4. SENTRY Bước 1 — FILTER mạng
1 verdict Block/Allow cho mọi request (`Services/SentryService.cs` + `Services/SentryLists.cs`), ~3µs/lần, fail-open.
Blocklist ~6.300 domain (EasyList/EasyPrivacy curate: chỉ rule third-party/bare-adserver, bỏ domain
nhạy cảm login/pay, bỏ subdomain bị bao phủ; không gồm tag-manager/consent để khỏi vỡ trang).
First-party không bao giờ block.
Nút **S**: số đã chặn + tắt/bật theo site (`sentry.allow.json`).
- Live test vnexpress (list mới): 495→286 requests (−42%), RAM 747→512MB (−32%), nội dung còn 99,1% (không vỡ trang).
- Xem `docs/reports/Cardon-SENTRY-Report.html`.

## 6. Brave Shields — phần host làm được (`Services/ShieldUrls.cs`)
Tham khảo Brave nhưng trung thực về giới hạn: cookie/fingerprint/script-blocking cần patch
renderer nên Cardon KHÔNG làm. Chỉ làm 4 món host-side, fail-open:
- **Strip query tracker** (~50 param utm_*/fbclid/gclid...): navigation mình gọi thì làm sạch trước;
  document từ link ngoài thì 302 về URL sạch (chỉ redirect khi URL đổi nên không lặp).
- **Debounce**: google `/url?q=`, facebook `l.php?u=` đi thẳng đích, khỏi bounce-tracker set cookie.
  Short-link (bit.ly...) không đoán — để nguyên.
- **HTTPS upgrade**: `http://` → thử `https://` trước, lỗi thì về `http` đúng 1 lần (chống lặp bằng cờ).
- **Referer**: cross-origin cắt còn origin, same-origin giữ nguyên (tránh vỡ hotlink/CSRF check).
- **Memory Saver kiểu single-view**: minimize cửa sổ → `MemoryUsageTargetLevel.Low`, mở lại → Normal.
- Live verify: httpbin chỉ nhận `page=2` sau strip; strip không lặp/không vỡ trang.

## 5. GPU — ít frame + tắt video nền (`Services/MediaPauser.cs`)
GPU process to vì subframe quảng cáo chạy video/canvas. Xử lý 2 lớp:
- Lớp 1 (xong): blocklist 6.3k làm sập 60→5 frames trên vnexpress (đo thật, recaptcha giữ lại đúng).
- Lớp 2 (mới): pauser chèn mọi frame — pause video **muted ngoài viewport**, không động video có tiếng
  (cần gesture nên là ý định user) và video trong viewport. Test synthetic: muted-offscreen paused ✓,
  2 loại còn lại playing ✓. Site allowlisted thì không arm.
- Không dùng `--disable-gpu` (đẩy việc sang CPU, tốn pin hơn). Không capping được GPU process từ host —
  chỉ bớt việc cho nó.

## 7. Data Saver kiểu client (không cần server như Opera Mini)
Opera Mini nén 90% nhờ proxy server farm (OBML + recompress ảnh + chạy JS hộ) — đổi lại mất mã hóa
đầu-cuối trên HTTPS và tốn hạ tầng. Cardon không có farm nên không bắt chước; làm phần client miễn phí:
- Header `Save-Data: on` mọi request (server/CDN nào hỗ trợ sẽ trả bản nhẹ: ảnh nhỏ, không font).
  Đã verify header đi thật qua httpbin echo.
- Chèn `loading=lazy` + `decoding=async` cho ảnh/iframe dưới sâu chưa có thuộc tính (chung 1 script
  với MediaPauser). Trung thực: preload scanner thường đi trước observer nên chỉ cứu một phần —
  đo thật vnexpress (vốn đã lazy sẵn): −4,8% bytes ban đầu (~19KB). Trang không lazy sẵn hưởng nhiều hơn.
