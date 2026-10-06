namespace KmyBrowser.Services;

// Cardon Browser — smart address bar.
// CUSTOMIZE: sửa logic tại đây.
public static class Navigator
{
    // Canonical Cardon URIs (giữ kmy:// để tương thích bản cũ)
    public const string DashboardUri = "cardon://dashboard";
    public const string AboutUri = "cardon://about";
    public const string CacheUri = "cardon://cache";
    public const string BlankUri = "about:blank";

    private const string LegacyDashboard = "kmy://dashboard";
    private const string LegacyAbout = "kmy://about";

    public static bool IsInternal(string url) =>
        url.StartsWith("cardon://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("kmy://", StringComparison.OrdinalIgnoreCase) ||
        url.Equals("about:blank", StringComparison.OrdinalIgnoreCase);

    public static string NormalizeInternal(string url)
    {
        if (string.Equals(url, LegacyDashboard, StringComparison.OrdinalIgnoreCase)) return DashboardUri;
        if (string.Equals(url, LegacyAbout, StringComparison.OrdinalIgnoreCase)) return AboutUri;
        return url;
    }

    // Input -> URL: giữ nguyên http(s), domain tự thêm https://, còn lại search.
    public static string ResolveInputToUrl(string input, SettingsService settings)
    {
        input = (input ?? "").Trim();
        if (string.IsNullOrEmpty(input)) return DashboardUri;
        input = NormalizeInternal(input);
        if (string.Equals(input, DashboardUri, StringComparison.OrdinalIgnoreCase)) return DashboardUri;
        if (string.Equals(input, AboutUri, StringComparison.OrdinalIgnoreCase)) return AboutUri;
        if (string.Equals(input, CacheUri, StringComparison.OrdinalIgnoreCase)) return CacheUri;
        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return input;

        bool looksLikeDomain = !input.Contains(' ') && input.Contains('.') && !input.Contains("://");
        bool looksLikeLocalhost = input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase);
        if (looksLikeDomain || looksLikeLocalhost)
            return "https://" + input;

        return settings.BuildSearchUrl(input);
    }
}
