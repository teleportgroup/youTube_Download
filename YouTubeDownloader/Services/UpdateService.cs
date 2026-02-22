using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace YouTubeDownloader.Services;

public class UpdateInfo
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public GitHubAsset[] Assets { get; set; } = [];

    public Version? Version => ParseVersion(TagName);

    private static Version? ParseVersion(string tag)
    {
        var versionString = tag.TrimStart('v', 'V');
        return Version.TryParse(versionString, out var version) ? version : null;
    }
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class UpdateService
{
    private static UpdateService? _instance;
    public static UpdateService Instance => _instance ??= new UpdateService();

    private readonly HttpClient _httpClient;
    private readonly string _repoOwner;
    private readonly string _repoName;

    public Version CurrentVersion { get; }
    public UpdateInfo? LatestRelease { get; private set; }
    public bool UpdateAvailable => LatestRelease?.Version > CurrentVersion;

    public event EventHandler<UpdateInfo>? UpdateFound;

    public UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("YouTubeAudioDownloader/1.0");

        // Get current version from assembly
        var assembly = Assembly.GetExecutingAssembly();
        CurrentVersion = assembly.GetName().Version ?? new Version(1, 0, 0);

        // Parse repo from RepositoryUrl in assembly metadata
        var repoUrl = GetRepositoryUrl();
        (_repoOwner, _repoName) = ParseGitHubUrl(repoUrl);
    }

    private static string GetRepositoryUrl()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var attribute = assembly.GetCustomAttribute<AssemblyMetadataAttribute>();

        // Try to get from assembly metadata
        foreach (var attr in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (attr.Key == "RepositoryUrl")
                return attr.Value ?? string.Empty;
        }

        // Fallback - should be set in .csproj
        return string.Empty;
    }

    private static (string owner, string repo) ParseGitHubUrl(string url)
    {
        // Parse https://github.com/OWNER/REPO
        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length >= 2)
                return (segments[0], segments[1]);
        }
        catch { }

        return ("OWNER", "REPO");
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        if (_repoOwner == "OWNER" || _repoName == "REPO")
            return null;

        try
        {
            var url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            LatestRelease = await response.Content.ReadFromJsonAsync<UpdateInfo>();

            if (UpdateAvailable && LatestRelease != null)
            {
                UpdateFound?.Invoke(this, LatestRelease);
            }

            return LatestRelease;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> DownloadUpdateAsync(IProgress<double>? progress = null)
    {
        if (LatestRelease == null) return null;

        // Find the installer asset
        var installerAsset = Array.Find(LatestRelease.Assets,
            a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                 a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase));

        if (installerAsset == null) return null;

        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), installerAsset.Name);

            using var response = await _httpClient.GetAsync(installerAsset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? installerAsset.Size;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = File.Create(tempPath);

            var buffer = new byte[8192];
            var totalRead = 0L;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;
                progress?.Report((double)totalRead / totalBytes * 100);
            }

            return tempPath;
        }
        catch
        {
            return null;
        }
    }

    public void LaunchInstallerAndExit(string installerPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true
        });

        Environment.Exit(0);
    }
}
