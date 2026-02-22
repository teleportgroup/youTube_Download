using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace YouTubeDownloader.Models;

public enum DownloadStatus
{
    Pending,
    FetchingInfo,
    Downloading,
    Converting,
    Completed,
    Error,
    Cancelled
}

public partial class DownloadItem : ObservableObject
{
    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _title = "Fetching info...";

    [ObservableProperty]
    private DownloadStatus _status = DownloadStatus.Pending;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    public Guid Id { get; } = Guid.NewGuid();

    public CancellationTokenSource? CancellationTokenSource { get; set; }

    public string StatusText => Status switch
    {
        DownloadStatus.Pending => "Pending",
        DownloadStatus.FetchingInfo => "Fetching info...",
        DownloadStatus.Downloading => $"Downloading {Progress:F0}%",
        DownloadStatus.Converting => "Converting to MP3...",
        DownloadStatus.Completed => "Completed",
        DownloadStatus.Error => $"Error: {ErrorMessage}",
        DownloadStatus.Cancelled => "Cancelled",
        _ => "Unknown"
    };

    partial void OnStatusChanged(DownloadStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnProgressChanged(double value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnErrorMessageChanged(string value)
    {
        OnPropertyChanged(nameof(StatusText));
    }
}
