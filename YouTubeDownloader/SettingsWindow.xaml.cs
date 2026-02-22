using System.Windows;
using System.Windows.Controls;
using YouTubeDownloader.Models;
using YouTubeDownloader.Services;

namespace YouTubeDownloader;

public partial class SettingsWindow : Window
{
    private readonly DownloadSettings? _downloadSettings;

    public SettingsWindow()
    {
        InitializeComponent();
        LoadCurrentTheme();
    }

    public SettingsWindow(DownloadSettings downloadSettings) : this()
    {
        _downloadSettings = downloadSettings;
        LoadDownloadSettings();
    }

    private void LoadCurrentTheme()
    {
        var currentTheme = ThemeService.Instance.CurrentThemeSetting;
        foreach (ComboBoxItem item in ThemeComboBox.Items)
        {
            if (item.Tag?.ToString() == currentTheme.ToString())
            {
                ThemeComboBox.SelectedItem = item;
                break;
            }
        }
    }

    private void LoadDownloadSettings()
    {
        if (_downloadSettings == null) return;

        foreach (ComboBoxItem item in ConcurrentDownloadsComboBox.Items)
        {
            if (item.Tag?.ToString() == _downloadSettings.MaxConcurrentDownloads.ToString())
            {
                ConcurrentDownloadsComboBox.SelectedItem = item;
                break;
            }
        }
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem selected && selected.Tag is string themeTag)
        {
            if (System.Enum.TryParse<AppTheme>(themeTag, out var theme))
            {
                ThemeService.Instance.CurrentThemeSetting = theme;
            }
        }
    }

    private void ConcurrentDownloadsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_downloadSettings == null) return;

        if (ConcurrentDownloadsComboBox.SelectedItem is ComboBoxItem selected && selected.Tag is string countTag)
        {
            if (int.TryParse(countTag, out var count))
            {
                _downloadSettings.MaxConcurrentDownloads = count;
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
