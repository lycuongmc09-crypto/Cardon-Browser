# Cardon Browser — Sharp. Swift. Yours.

Lightweight WebView2 browser (C# WPF), rebuilt độc lập với bản sắc riêng: **carbon + amber**.

## Chạy
```
dotnet run --project KMY-Browser-Custom
# Release:
KMY-Browser-Custom\bin\Release\net8.0-windows\CardonBrowser.exe
```
Yêu cầu: Windows 10/11 + WebView2 Runtime.

## Đặc trưng riêng của Cardon
- Branding **CARDON + logo C**, theme carbon/amber (khác hẳn peacock-teal bản gốc)
- Trang nội bộ riêng: `cardon://dashboard`, `cardon://about` (vẫn mở được `kmy://` cũ)
- Dashboard có quick-links + triết lý "Cut through the noise"
- Lịch sử file: `%AppData%/CardonBrowser/history.json`
- Settings file: `%AppData%/CardonBrowser/settings.json` — đổi engine (Brave/Google/Bing/DuckDuckGo), homepage (Dashboard/Blank/Custom)

## Tùy biến tiếp
| Muốn đổi... | Sửa |
|---|---|
| Màu UI/nav | `MainWindow.xaml` |
| Dashboard/About | `Services/Pages.cs` |
| Logic URL | `Services/Navigator.cs` |
| Engine mới | `Models/AppSettings.cs` |
| Chỗ lưu file | `Services/HistoryService.cs`, `SettingsService.cs` |
