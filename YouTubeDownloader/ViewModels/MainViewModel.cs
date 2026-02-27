using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using YouTubeDownloader.Models;
using YouTubeDownloader.Services;

namespace YouTubeDownloader.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDownloadService _downloadService;
    private SemaphoreSlim? _downloadSemaphore;

    [ObservableProperty]
    private string _singleUrl = string.Empty;

    [ObservableProperty]
    private string _batchUrls = string.Empty;

    [ObservableProperty]
    private DownloadSettings _settings = new();

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isInitializing = true;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private SearchResult? _selectedSearchResult;

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private string _updateVersion = string.Empty;

    public ObservableCollection<DownloadItem> Downloads { get; } = [];
    public ObservableCollection<SearchResult> SearchResults { get; } = [];

    public MainViewModel()
    {
        _downloadService = new YtDlpService();
    }

    public UpdateInfo? LatestUpdateInfo { get; private set; }

    public async Task InitializeAsync()
    {
        StatusMessage = "Checking dependencies...";
        var progress = new Progress<string>(msg => StatusMessage = msg);
        var success = await _downloadService.EnsureDependenciesAsync(progress);

        if (!success)
        {
            StatusMessage = "Failed to download dependencies. Please check your internet connection.";
        }

        IsInitializing = false;
    }

    public async Task CheckForUpdatesAsync()
    {
        try
        {
            var updateInfo = await UpdateService.Instance.CheckForUpdatesAsync();
            if (updateInfo != null && UpdateService.Instance.UpdateAvailable)
            {
                LatestUpdateInfo = updateInfo;
                UpdateAvailable = true;
                UpdateVersion = updateInfo.Version?.ToString(3) ?? "New";
            }
        }
        catch
        {
            // Silently ignore update check failures
        }
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Output Folder",
            InitialDirectory = Settings.OutputFolder
        };

        if (dialog.ShowDialog() == true)
        {
            Settings.OutputFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void PasteFromClipboard()
    {
        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText();
            if (text.Contains('\n'))
            {
                BatchUrls = string.IsNullOrEmpty(BatchUrls)
                    ? text
                    : BatchUrls + Environment.NewLine + text;
            }
            else
            {
                SingleUrl = text;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;

        IsSearching = true;
        SearchResults.Clear();
        StatusMessage = $"Searching for: {SearchQuery}...";

        try
        {
            var results = await _downloadService.SearchAsync(SearchQuery, 10);
            foreach (var result in results)
            {
                SearchResults.Add(result);
            }
            StatusMessage = $"Found {results.Count} results";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Search error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanSearch() => !IsSearching && !string.IsNullOrWhiteSpace(SearchQuery);

    [RelayCommand]
    private void AddSearchResultToQueue(SearchResult? result)
    {
        if (result == null) return;
        AddDownloadItem(result.Url, $"{result.Artist} - {result.DisplayTrack}");
    }

    [RelayCommand]
    private void AddAllSearchResultsToQueue()
    {
        foreach (var result in SearchResults)
        {
            AddDownloadItem(result.Url, $"{result.Artist} - {result.DisplayTrack}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartDownload))]
    private async Task StartDownloadAsync()
    {
        IsDownloading = true;
        StatusMessage = "Starting downloads...";

        try
        {
            var urls = GetAllUrls();

            foreach (var url in urls)
            {
                if (string.IsNullOrWhiteSpace(url)) continue;

                var trimmedUrl = url.Trim();
                if (!IsValidYouTubeUrl(trimmedUrl)) continue;

                if (Settings.DownloadPlaylist && IsPlaylistUrl(trimmedUrl))
                {
                    StatusMessage = "Fetching playlist...";
                    try
                    {
                        var playlistUrls = await _downloadService.GetPlaylistUrlsAsync(trimmedUrl);
                        foreach (var playlistUrl in playlistUrls)
                        {
                            AddDownloadItem(playlistUrl);
                        }
                    }
                    catch
                    {
                        AddDownloadItem(trimmedUrl);
                    }
                }
                else
                {
                    AddDownloadItem(trimmedUrl);
                }
            }

            await ProcessDownloadQueueAsync();
        }
        finally
        {
            IsDownloading = false;
            StatusMessage = "Downloads completed";
        }
    }

    private bool CanStartDownload()
    {
        return !IsDownloading && !IsInitializing &&
               (!string.IsNullOrWhiteSpace(SingleUrl) || !string.IsNullOrWhiteSpace(BatchUrls) || Downloads.Any(d => d.Status == DownloadStatus.Pending));
    }

    private string[] GetAllUrls()
    {
        var urls = new List<string>();

        if (!string.IsNullOrWhiteSpace(SingleUrl))
            urls.Add(SingleUrl);

        if (!string.IsNullOrWhiteSpace(BatchUrls))
            urls.AddRange(BatchUrls.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

        return urls.Distinct().ToArray();
    }

    private void AddDownloadItem(string url, string? title = null)
    {
        // Only block if URL is already pending or in progress (allow re-download in different format)
        if (Downloads.Any(d => d.Url == url && d.Status is DownloadStatus.Pending or DownloadStatus.FetchingInfo or DownloadStatus.Downloading or DownloadStatus.Converting)) return;

        var item = new DownloadItem
        {
            Url = url,
            Title = title ?? "Fetching info...",
            Status = DownloadStatus.Pending
        };

        Application.Current.Dispatcher.Invoke(() => Downloads.Add(item));
        StartDownloadCommand.NotifyCanExecuteChanged();
    }

    private async Task ProcessDownloadQueueAsync()
    {
        var pendingItems = Downloads.Where(d => d.Status == DownloadStatus.Pending).ToList();
        if (pendingItems.Count == 0) return;

        // Create semaphore for concurrent download limiting
        _downloadSemaphore = new SemaphoreSlim(Settings.MaxConcurrentDownloads);

        var downloadTasks = pendingItems.Select(item => ProcessSingleDownloadAsync(item)).ToList();
        await Task.WhenAll(downloadTasks);

        _downloadSemaphore.Dispose();
        _downloadSemaphore = null;
    }

    private async Task ProcessSingleDownloadAsync(DownloadItem item)
    {
        if (_downloadSemaphore == null) return;

        await _downloadSemaphore.WaitAsync();

        try
        {
            if (item.Status == DownloadStatus.Cancelled) return;

            item.CancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = item.CancellationTokenSource.Token;

            try
            {
                // Fetch video info if not already set
                if (item.Title == "Fetching info...")
                {
                    item.Status = DownloadStatus.FetchingInfo;
                    Application.Current.Dispatcher.Invoke(() => StatusMessage = $"Fetching: {item.Url}");

                    try
                    {
                        var info = await _downloadService.GetVideoInfoAsync(item.Url, cancellationToken);
                        if (info != null)
                        {
                            item.Title = Settings.UseArtistTitleNaming && !string.IsNullOrEmpty(info.Artist)
                                ? $"{info.Artist} - {info.DisplayTrack}"
                                : info.Title;
                        }
                        else
                        {
                            item.Title = "Unknown Title";
                        }
                    }
                    catch
                    {
                        item.Title = "Unknown Title";
                    }
                }

                item.Status = DownloadStatus.Downloading;
                Application.Current.Dispatcher.Invoke(() => StatusMessage = $"Downloading: {item.Title}");

                var progress = new Progress<double>(p =>
                {
                    item.Progress = p;
                    if (p >= 99)
                    {
                        item.Status = DownloadStatus.Converting;
                    }
                });

                await _downloadService.DownloadAsync(item, Settings, progress, cancellationToken);

                item.Progress = 100;
                item.Status = DownloadStatus.Completed;
                Application.Current.Dispatcher.Invoke(() => StatusMessage = $"Completed: {item.Title}");
            }
            catch (OperationCanceledException)
            {
                item.Status = DownloadStatus.Cancelled;
                Application.Current.Dispatcher.Invoke(() => StatusMessage = $"Cancelled: {item.Title}");
            }
            catch (Exception ex)
            {
                item.Status = DownloadStatus.Error;
                item.ErrorMessage = ex.Message;
                Application.Current.Dispatcher.Invoke(() => StatusMessage = $"Error: {item.Title}");
            }
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    [RelayCommand]
    private void CancelDownload(DownloadItem? item)
    {
        if (item == null) return;

        item.CancellationTokenSource?.Cancel();
        item.Status = DownloadStatus.Cancelled;
    }

    [RelayCommand]
    private void ClearCompleted()
    {
        var completed = Downloads
            .Where(d => d.Status is DownloadStatus.Completed or DownloadStatus.Error or DownloadStatus.Cancelled)
            .ToList();

        foreach (var item in completed)
        {
            Downloads.Remove(item);
        }

        SingleUrl = string.Empty;
        BatchUrls = string.Empty;
    }

    [RelayCommand]
    private void ClearSearchResults()
    {
        SearchResults.Clear();
        SearchQuery = string.Empty;
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", Settings.OutputFolder);
        }
        catch
        {
            // Ignore errors opening folder
        }
    }

    private static bool IsValidYouTubeUrl(string url)
    {
        return url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlaylistUrl(string url)
    {
        return url.Contains("list=", StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSingleUrlChanged(string value) => StartDownloadCommand.NotifyCanExecuteChanged();
    partial void OnBatchUrlsChanged(string value) => StartDownloadCommand.NotifyCanExecuteChanged();
    partial void OnIsDownloadingChanged(bool value) => StartDownloadCommand.NotifyCanExecuteChanged();
    partial void OnIsInitializingChanged(bool value) => StartDownloadCommand.NotifyCanExecuteChanged();
    partial void OnSearchQueryChanged(string value) => SearchCommand.NotifyCanExecuteChanged();
    partial void OnIsSearchingChanged(bool value) => SearchCommand.NotifyCanExecuteChanged();
}
