using System.Windows;
using YouTubeDownloader.Services;

namespace YouTubeDownloader;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeService.Instance.ApplyTheme();
    }
}
