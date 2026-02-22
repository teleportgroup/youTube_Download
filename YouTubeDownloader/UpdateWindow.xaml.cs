using System;
using System.Windows;
using YouTubeDownloader.Services;

namespace YouTubeDownloader;

public partial class UpdateWindow : Window
{
    private readonly UpdateService _updateService;

    public UpdateWindow(UpdateInfo updateInfo)
    {
        InitializeComponent();

        _updateService = UpdateService.Instance;

        CurrentVersionText.Text = _updateService.CurrentVersion.ToString(3);
        NewVersionText.Text = updateInfo.Version?.ToString(3) ?? updateInfo.TagName;
        ChangelogText.Text = string.IsNullOrWhiteSpace(updateInfo.Body)
            ? "No changelog provided."
            : updateInfo.Body;
    }

    private void RemindLater_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;
        RemindLaterButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;

        var progress = new Progress<double>(p =>
        {
            DownloadProgress.Value = p;
            ProgressText.Text = $"Downloading update... {p:F0}%";
        });

        try
        {
            var installerPath = await _updateService.DownloadUpdateAsync(progress);

            if (installerPath != null)
            {
                ProgressText.Text = "Download complete. Launching installer...";
                _updateService.LaunchInstallerAndExit(installerPath);
            }
            else
            {
                ProgressText.Text = "Download failed. Please try again or download manually.";
                DownloadButton.IsEnabled = true;
                RemindLaterButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            ProgressText.Text = $"Error: {ex.Message}";
            DownloadButton.IsEnabled = true;
            RemindLaterButton.IsEnabled = true;
        }
    }
}
