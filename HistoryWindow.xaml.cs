using System.Windows;
using System.Windows.Input;
using KmyBrowser.Models;
using KmyBrowser.Services;

namespace KmyBrowser;

public partial class HistoryWindow : Window
{
    private readonly HistoryService _history;
    public event Action<string>? UrlChosen;

    public HistoryWindow(HistoryService history)
    {
        InitializeComponent();
        _history = history;
        Refresh("");
    }

    private void Refresh(string filter)
    {
        IEnumerable<HistoryEntry> items = _history.Entries;
        if (!string.IsNullOrWhiteSpace(filter))
            items = items.Where(e =>
                e.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                e.Url.Contains(filter, StringComparison.OrdinalIgnoreCase));
        List.ItemsSource = items.ToList();
    }

    private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => Refresh(FilterBox.Text.Trim());

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (List.SelectedItem is HistoryEntry entry)
        {
            UrlChosen?.Invoke(entry.Url);
            Close();
        }
    }

    private void ClearBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Xóa toàn bộ lịch sử?", "Cardon Browser",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _history.Clear();
            Refresh(FilterBox.Text.Trim());
        }
    }
}
