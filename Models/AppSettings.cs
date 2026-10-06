namespace KmyBrowser.Models;

public sealed class AppSettings
{
    // Search engine URL template, {q} = query. CUSTOMIZE: thêm engine mới ở SettingsWindow.
    public string SearchEngineName { get; set; } = "Brave";
    public string SearchUrlTemplate { get; set; } = "https://search.brave.com/search?q={q}";

    // HomepageMode: "Dashboard" | "Blank" | "Custom"
    public string HomepageMode { get; set; } = "Dashboard";
    public string CustomHomepageUrl { get; set; } = "https://www.google.com";

    public static Dictionary<string, string> BuiltinEngines { get; } = new()
    {
        ["Brave"] = "https://search.brave.com/search?q={q}",
        ["Google"] = "https://www.google.com/search?q={q}",
        ["Bing"] = "https://www.bing.com/search?q={q}",
        ["DuckDuckGo"] = "https://duckduckgo.com/?q={q}",
    };
}
