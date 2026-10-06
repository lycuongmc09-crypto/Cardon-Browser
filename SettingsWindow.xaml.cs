using System.Windows;
using System.Windows.Controls;
using KmyBrowser.Models;
using KmyBrowser.Services;

namespace KmyBrowser;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly HistoryService _history;

    public SettingsWindow(SettingsService settings, HistoryService history)
    {
        InitializeComponent();
        _settings = settings;
        _history = history;

        foreach (var name in AppSettings.BuiltinEngines.Keys)
            EngineBox.Items.Add(name);
        EngineBox.SelectedItem = _settings.Current.SearchEngineName;
        if (EngineBox.SelectedItem is null) EngineBox.SelectedIndex = 0;

        foreach (ComboBoxItem item in HomeBox.Items)
            if (string.Equals((string)item.Content, _settings.Current.HomepageMode, StringComparison.OrdinalIgnoreCase))
                HomeBox.SelectedItem = item;
        if (HomeBox.SelectedItem is null) HomeBox.SelectedIndex = 0;

        CustomUrlBox.Text = _settings.Current.CustomHomepageUrl;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var engine = (EngineBox.SelectedItem as string) ?? "Brave";
        _settings.Current.SearchEngineName = engine;
        _settings.Current.SearchUrlTemplate = AppSettings.BuiltinEngines.TryGetValue(engine, out var tpl)
            ? tpl : AppSettings.BuiltinEngines["Brave"];
        _settings.Current.HomepageMode = ((ComboBoxItem)HomeBox.SelectedItem).Content as string ?? "Dashboard";
        _settings.Current.CustomHomepageUrl = CustomUrlBox.Text.Trim();
        _settings.Save();
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        MessageBox.Show("Đã xóa lịch sử.", "Cardon Browser");
    }
}
