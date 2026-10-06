# KMY Browser - Custom (C# WPF + WebView2)

Bản viết lại từ KMY-Browser gốc (chỉ có .exe) để **tùy biến sâu**, giữ triết lý nhẹ + WebView2.

## Chạy
```
dotnet run --project KMY-Browser-Custom
# hoặc bản Release:
KMY-Browser-Custom\bin\Release\net8.0-windows\KmyBrowser.exe
```
Yêu cầu: Windows 10/11 + WebView2 Runtime (máy bạn đã có 150.x).

## Tính năng
- Back / Forward / Reload / Home
- Smart address bar: URL giữ nguyên, `example.com` -> `https://`, còn lại search theo engine
- Dashboard `kmy://dashboard`, About `kmy://about`, Blank `about:blank`
- Loading bar, title tự cập nhật
- **Lịch sử bền vững**: `%AppData%/KmyBrowser/history.json` (1000 mục, không mất khi restart). Double-click để mở lại, có filter + Clear.
- **Settings**: search engine (Brave/Google/Bing/DuckDuckGo) + homepage (Dashboard/Blank/Custom). Lưu ở `%AppData%/KmyBrowser/settings.json`.

## Tùy biến sâu ở đâu?
| Muốn đổi... | Sửa file |
|---|---|
| Màu UI, nút nav | `MainWindow.xaml` |
| Logic URL/search | `Services/Navigator.cs` |
| Dashboard/About HTML | `Services/Pages.cs` |
| Thêm search engine | `Models/AppSettings.cs` -> `BuiltinEngines` |
| Đổi chỗ lưu file, giới hạn 1000 | `Services/HistoryService.cs`, `Services/SettingsService.cs` |
| Thêm tab/bookmark/adblock | Tạo thêm `TabService`, gắn `WebView2` động trong `MainWindow` |

## Khác bản gốc
- Gốc: C++ Win32, history RAM (mất khi tắt), không settings.
- Bản này: C# WPF, history file JSON, có Settings page. Dễ thêm tab/bookmark/theme hơn nhiều.
