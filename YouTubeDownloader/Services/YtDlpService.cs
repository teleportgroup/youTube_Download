using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YouTubeDownloader.Models;

namespace YouTubeDownloader.Services;

public partial class YtDlpService : IDownloadService
{
    private readonly DependencyChecker _dependencyChecker;
    private static readonly HttpClient _httpClient = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36" }
        }
    };

    public YtDlpService()
    {
        _dependencyChecker = new DependencyChecker();
    }

    public async Task<bool> EnsureDependenciesAsync(IProgress<string>? statusProgress = null)
    {
        return await _dependencyChecker.EnsureDependenciesAsync(statusProgress);
    }

    public async Task<string> GetVideoTitleAsync(string url, CancellationToken cancellationToken = default)
    {
        var info = await GetVideoInfoAsync(url, cancellationToken);
        return info?.Title ?? "Unknown Title";
    }

    public async Task<VideoInfo?> GetVideoInfoAsync(string url, CancellationToken cancellationToken = default)
    {
        var args = $"--dump-json --no-playlist \"{url}\"";
        var output = await RunYtDlpAsync(args, cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            return new VideoInfo
            {
                Title = GetJsonString(root, "title") ?? "Unknown Title",
                Track = GetJsonString(root, "track") ?? string.Empty,  // Clean track name
                Artist = GetJsonString(root, "artist") ?? GetJsonString(root, "uploader") ?? "Unknown Artist",
                Duration = FormatDuration(GetJsonInt(root, "duration")),
                Thumbnail = GetJsonString(root, "thumbnail") ?? string.Empty
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<string>> GetPlaylistUrlsAsync(string url, CancellationToken cancellationToken = default)
    {
        var urls = new List<string>();
        var args = $"--flat-playlist --dump-json \"{url}\"";
        var output = await RunYtDlpAsync(args, cancellationToken);

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("url", out var urlProp))
                {
                    var videoUrl = urlProp.GetString();
                    if (!string.IsNullOrEmpty(videoUrl))
                    {
                        if (!videoUrl.StartsWith("http"))
                            videoUrl = $"https://www.youtube.com/watch?v={videoUrl}";
                        urls.Add(videoUrl);
                    }
                }
                else if (doc.RootElement.TryGetProperty("id", out var idProp))
                {
                    var videoId = idProp.GetString();
                    if (!string.IsNullOrEmpty(videoId))
                    {
                        urls.Add($"https://www.youtube.com/watch?v={videoId}");
                    }
                }
            }
            catch
            {
                // Skip invalid JSON lines
            }
        }

        return urls;
    }

    public async Task<List<SearchResult>> SearchAsync(string query, int maxResults = 10, CancellationToken cancellationToken = default)
    {
        var videoIds = new HashSet<string>();

        // Phase 1a: Search DuckDuckGo for label releases (most reliable for finding "Provided to YouTube" content)
        try
        {
            var ddgIds = await SearchDuckDuckGoAsync(query, cancellationToken);
            foreach (var id in ddgIds)
                videoIds.Add(id);
        }
        catch
        {
            // DuckDuckGo search failed, continue with other sources
        }

        // Phase 1b: Search regular YouTube
        try
        {
            var searchCount = Math.Min(maxResults * 2, 20);
            var youtubeArgs = $"--dump-json --flat-playlist --no-download \"ytsearch{searchCount}:{query}\"";
            var youtubeOutput = await RunYtDlpAsync(youtubeArgs, cancellationToken);

            foreach (var line in youtubeOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var id = GetJsonString(doc.RootElement, "id");
                    if (!string.IsNullOrEmpty(id))
                        videoIds.Add(id);
                }
                catch { }
            }
        }
        catch
        {
            // YouTube search failed, continue with other sources
        }

        // Phase 1c: Also search YouTube Music to find official Topic channel versions
        try
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var ytMusicArgs = $"--dump-json --flat-playlist --no-download \"https://music.youtube.com/search?q={encodedQuery}\"";
            var ytMusicOutput = await RunYtDlpAsync(ytMusicArgs, cancellationToken);

            foreach (var line in ytMusicOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var id = GetJsonString(doc.RootElement, "id");
                    // Only add video IDs (skip channel/playlist IDs which are longer)
                    if (!string.IsNullOrEmpty(id) && id.Length == 11)
                        videoIds.Add(id);
                }
                catch { }
            }
        }
        catch
        {
            // YouTube Music search failed, continue with other sources
        }

        // Phase 2: Fetch full info for each video (in parallel) to get descriptions
        var results = new List<SearchResult>();
        var tasks = videoIds.Select(id => GetVideoDetailsForSearchAsync(id, cancellationToken)).ToList();
        var videoDetails = await Task.WhenAll(tasks);

        foreach (var result in videoDetails.Where(r => r != null))
        {
            results.Add(result!);
        }

        // Phase 3: Sort results - official audio first, then others
        var sortedResults = results
            .OrderByDescending(r => r.IsOfficialAudio)
            .ThenBy(r => r.Title)
            .Take(maxResults)
            .ToList();

        return sortedResults;
    }

    private async Task<List<string>> SearchDuckDuckGoAsync(string query, CancellationToken cancellationToken)
    {
        var videoIds = new List<string>();

        // Search DuckDuckGo HTML for YouTube videos with "Provided to YouTube" in description
        var searchQuery = Uri.EscapeDataString($"site:youtube.com {query} \"Provided to YouTube\"");
        var url = $"https://html.duckduckgo.com/html/?q={searchQuery}";

        var response = await _httpClient.GetStringAsync(url, cancellationToken);

        // Extract YouTube video IDs from the response
        var matches = YouTubeIdRegex().Matches(response);
        foreach (Match match in matches)
        {
            var id = match.Groups[1].Value;
            if (!videoIds.Contains(id))
                videoIds.Add(id);
        }

        return videoIds;
    }

    private async Task<SearchResult?> GetVideoDetailsForSearchAsync(string videoId, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://www.youtube.com/watch?v={videoId}";
            var args = $"--dump-json --no-playlist \"{url}\"";
            var output = await RunYtDlpAsync(args, cancellationToken);

            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            var description = GetJsonString(root, "description") ?? string.Empty;
            var isOfficialAudio = description.StartsWith("Provided to YouTube by", StringComparison.OrdinalIgnoreCase);

            return new SearchResult
            {
                Id = videoId,
                Title = GetJsonString(root, "title") ?? "Unknown",
                Track = GetJsonString(root, "track") ?? string.Empty,
                Artist = GetJsonString(root, "artist") ?? GetJsonString(root, "uploader") ?? "Unknown",
                Url = url,
                Duration = FormatDuration(GetJsonInt(root, "duration")),
                Thumbnail = GetJsonString(root, "thumbnail") ?? string.Empty,
                Description = description.Length > 200 ? description[..200] + "..." : description,
                IsOfficialAudio = isOfficialAudio
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task DownloadAsync(DownloadItem item, DownloadSettings settings, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(settings.OutputFolder);

        var outputTemplate = settings.GetOutputTemplate(settings.OutputFolder);
        var format = settings.GetYtDlpFormat();

        var argsBuilder = new StringBuilder();
        argsBuilder.Append($"-x --audio-format {format} ");

        // Bitrate setting (not applicable for lossless formats)
        if (settings.AudioFormat is AudioFormat.MP3 or AudioFormat.AAC or AudioFormat.Opus)
        {
            argsBuilder.Append($"--audio-quality {settings.AudioBitrate}K ");
        }

        argsBuilder.Append($"--ffmpeg-location \"{_dependencyChecker.FfmpegPath}\" ");
        argsBuilder.Append("--no-playlist ");
        argsBuilder.Append($"-o \"{outputTemplate}\" ");
        argsBuilder.Append("--newline ");

        // Metadata embedding
        if (settings.EmbedMetadata)
        {
            argsBuilder.Append("--embed-metadata ");
            argsBuilder.Append("--add-metadata ");
        }

        // Thumbnail embedding (requires mutagen for some formats)
        if (settings.EmbedThumbnail)
        {
            argsBuilder.Append("--embed-thumbnail ");
            // Convert thumbnail to compatible format
            argsBuilder.Append("--convert-thumbnails jpg ");
        }

        // Sanitize filename (remove illegal characters)
        argsBuilder.Append("--windows-filenames ");

        argsBuilder.Append($"\"{item.Url}\"");

        var outputBuilder = new StringBuilder();
        await RunYtDlpWithProgressAsync(argsBuilder.ToString(), progress, outputBuilder, cancellationToken);

        // Try to find the output file path
        var output = outputBuilder.ToString();
        var match = DestinationRegex().Match(output);
        if (match.Success)
        {
            item.OutputPath = match.Groups[1].Value;
        }
        else
        {
            // Try alternative pattern for when conversion happens
            match = MergerRegex().Match(output);
            if (match.Success)
            {
                item.OutputPath = match.Groups[1].Value;
            }
        }
    }

    private async Task<string> RunYtDlpAsync(string arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _dependencyChecker.YtDlpPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            }
        };

        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return output;
    }

    private async Task RunYtDlpWithProgressAsync(string arguments, IProgress<double> progress, StringBuilder outputBuilder, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _dependencyChecker.YtDlpPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            }
        };

        process.Start();

        var outputTask = Task.Run(async () =>
        {
            while (!process.StandardOutput.EndOfStream)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line != null)
                {
                    outputBuilder.AppendLine(line);
                    ParseProgress(line, progress);
                }
            }
        }, cancellationToken);

        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await Task.WhenAll(outputTask, errorTask);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await errorTask;
            if (!string.IsNullOrWhiteSpace(error) && !error.Contains("WARNING"))
            {
                throw new Exception($"yt-dlp error: {error}");
            }
        }
    }

    private static void ParseProgress(string line, IProgress<double> progress)
    {
        var match = ProgressRegex().Match(line);
        if (match.Success && double.TryParse(match.Groups[1].Value, out var percent))
        {
            progress.Report(percent);
        }
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static int GetJsonInt(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                // Handle both int and float values
                if (prop.TryGetInt32(out var intValue))
                    return intValue;
                if (prop.TryGetDouble(out var doubleValue))
                    return (int)doubleValue;
            }
        }
        return 0;
    }

    private static string FormatDuration(int seconds)
    {
        if (seconds <= 0) return "0:00";
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    [GeneratedRegex(@"\[download\]\s+(\d+\.?\d*)%")]
    private static partial Regex ProgressRegex();

    [GeneratedRegex(@"\[ExtractAudio\] Destination: (.+)$", RegexOptions.Multiline)]
    private static partial Regex DestinationRegex();

    [GeneratedRegex(@"\[Merger\] Merging formats into ""(.+)""", RegexOptions.Multiline)]
    private static partial Regex MergerRegex();

    [GeneratedRegex(@"youtube\.com/watch\?v=([a-zA-Z0-9_-]{11})")]
    private static partial Regex YouTubeIdRegex();
}
