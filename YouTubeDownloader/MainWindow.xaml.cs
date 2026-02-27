using System.Windows;
using YouTubeDownloader.ViewModels;

namespace YouTubeDownloader;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();

        // Check for updates in background after app loads
        _ = CheckForUpdatesInBackground();
    }

    private async System.Threading.Tasks.Task CheckForUpdatesInBackground()
    {
        // Small delay to let the UI settle
        await System.Threading.Tasks.Task.Delay(2000);
        await _viewModel.CheckForUpdatesAsync();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_viewModel.Settings)
        {
            Owner = this
        };
        settingsWindow.ShowDialog();
    }

    private void UpdateNotice_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.LatestUpdateInfo != null)
        {
            var updateWindow = new UpdateWindow(_viewModel.LatestUpdateInfo)
            {
                Owner = this
            };
            updateWindow.ShowDialog();
        }
    }
}
