# Cardon — Thuật toán tăng tốc (ghi chú kỹ thuật, tiếng Việt)

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
1 verdict Block/Allow cho mọi request (`Services/SentryService.cs`), ~3µs/lần, fail-open.
Blocklist ~72 tracker/ads (không gồm tag-manager/consent). First-party không bao giờ block.
Nút **S**: số đã chặn + tắt/bật theo site (`sentry.allow.json`).
- Live test (báo VN): vnexpress 483→326, dantri 241→164, tuoitre 367→281 requests.
- Xem `docs/reports/Cardon-SENTRY-Report.html`.
