using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        var results = new List<SearchResult>();

        // Append "topic" to find official audio channels
        var searchQuery = $"{query} topic";
        var args = $"--dump-json --flat-playlist --no-download \"ytsearch{maxResults}:{searchQuery}\"";
        var output = await RunYtDlpAsync(args, cancellationToken);

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var id = GetJsonString(root, "id");
                if (string.IsNullOrEmpty(id)) continue;

                results.Add(new SearchResult
                {
                    Id = id,
                    Title = GetJsonString(root, "title") ?? "Unknown",
                    Artist = GetJsonString(root, "uploader") ?? GetJsonString(root, "channel") ?? "Unknown",
                    Url = $"https://www.youtube.com/watch?v={id}",
                    Duration = FormatDuration(GetJsonInt(root, "duration")),
                    Thumbnail = GetJsonString(root, "thumbnail") ?? string.Empty
                });
            }
            catch
            {
                // Skip invalid JSON
            }
        }

        return results;
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
}
