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
        LoadVersionInfo();
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

    private void LoadVersionInfo()
    {
        VersionText.Text = UpdateService.Instance.CurrentVersion.ToString(3);
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

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking for updates...";

        var updateInfo = await UpdateService.Instance.CheckForUpdatesAsync();

        if (updateInfo == null)
        {
            UpdateStatusText.Text = "Could not check for updates. Please try again later.";
        }
        else if (UpdateService.Instance.UpdateAvailable)
        {
            UpdateStatusText.Text = $"Version {updateInfo.Version} available!";

            var updateWindow = new UpdateWindow(updateInfo)
            {
                Owner = this
            };
            updateWindow.ShowDialog();
        }
        else
        {
            UpdateStatusText.Text = "You're running the latest version.";
        }

        CheckUpdatesButton.IsEnabled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
