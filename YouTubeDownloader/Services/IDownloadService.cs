using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using YouTubeDownloader.Models;

namespace YouTubeDownloader.Services;

public class SearchResult
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string Thumbnail { get; set; } = string.Empty;
}

public interface IDownloadService
{
    Task<string> GetVideoTitleAsync(string url, CancellationToken cancellationToken = default);
    Task<VideoInfo?> GetVideoInfoAsync(string url, CancellationToken cancellationToken = default);
    Task<List<string>> GetPlaylistUrlsAsync(string url, CancellationToken cancellationToken = default);
    Task DownloadAsync(DownloadItem item, DownloadSettings settings, IProgress<double> progress, CancellationToken cancellationToken = default);
    Task<List<SearchResult>> SearchAsync(string query, int maxResults = 10, CancellationToken cancellationToken = default);
    Task<bool> EnsureDependenciesAsync(IProgress<string>? statusProgress = null);
}

public class VideoInfo
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string Thumbnail { get; set; } = string.Empty;
}
