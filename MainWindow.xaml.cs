using System.Windows;
using System.Windows.Input;
using KmyBrowser.Services;

namespace KmyBrowser;

public partial class MainWindow : Window
{
    private readonly HistoryService _history = new();
    private readonly SettingsService _settings = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings.Load();
        _history.Load();
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

        Browser.NavigationStarting += (_, _) => LoadingBar.Visibility = Visibility.Visible;
        Browser.NavigationCompleted += (_, _) =>
        {
            LoadingBar.Visibility = Visibility.Collapsed;
            var url = Browser.Source?.ToString() ?? "";
            AddressBar.Text = url;
            if (!Navigator.IsInternal(url))
                _history.Add(Browser.CoreWebView2?.DocumentTitle ?? url, url);
            UpdateTitle();
        };
        Browser.SourceChanged += (_, _) =>
        {
            var url = Browser.Source?.ToString() ?? "";
            AddressBar.Text = url;
        };

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

    private void NavigateTo(string url)
    {
        url = Navigator.NormalizeInternal(url);
        if (url == Navigator.DashboardUri)
        {
            Browser.NavigateToString(Pages.Dashboard());
            AddressBar.Text = Navigator.DashboardUri;
            return;
        }
        if (url == Navigator.AboutUri)
        {
            Browser.NavigateToString(Pages.About());
            AddressBar.Text = Navigator.AboutUri;
            return;
        }
        try { Browser.CoreWebView2?.Navigate(url); }
        catch { Browser.Source = new Uri(url); }
    }

    private void GoFromBar() => NavigateTo(Navigator.ResolveInputToUrl(AddressBar.Text, _settings));

    private void GoBtn_Click(object sender, RoutedEventArgs e) => GoFromBar();
    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) GoFromBar();
    }
    private void BackBtn_Click(object sender, RoutedEventArgs e)
    { try { if (Browser.CanGoBack) Browser.GoBack(); } catch { } }
    private void FwdBtn_Click(object sender, RoutedEventArgs e)
    { try { if (Browser.CanGoForward) Browser.GoForward(); } catch { } }
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

    // Cho HistoryWindow double-click dùng chung
    public void NavigatePublic(string url) => NavigateTo(url);
}
