using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace YouTubeDownloader.Services;

public class DependencyChecker
{
    private readonly string _appDirectory;
    private readonly HttpClient _httpClient;

    public string YtDlpPath => Path.Combine(_appDirectory, "yt-dlp.exe");
    public string FfmpegPath => Path.Combine(_appDirectory, "ffmpeg.exe");

    public DependencyChecker()
    {
        _appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("YouTubeDownloader/1.0");
    }

    public bool IsYtDlpInstalled => File.Exists(YtDlpPath);
    public bool IsFfmpegInstalled => File.Exists(FfmpegPath);

    public async Task<bool> EnsureDependenciesAsync(IProgress<string>? progress = null)
    {
        try
        {
            if (!IsYtDlpInstalled)
            {
                progress?.Report("Downloading yt-dlp...");
                await DownloadYtDlpAsync();
            }

            if (!IsFfmpegInstalled)
            {
                progress?.Report("Downloading ffmpeg...");
                await DownloadFfmpegAsync();
            }

            progress?.Report("All dependencies ready!");
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report($"Error downloading dependencies: {ex.Message}");
            return false;
        }
    }

    private async Task DownloadYtDlpAsync()
    {
        const string ytDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

        using var response = await _httpClient.GetAsync(ytDlpUrl);
        response.EnsureSuccessStatusCode();

        await using var fileStream = File.Create(YtDlpPath);
        await response.Content.CopyToAsync(fileStream);
    }

    private async Task DownloadFfmpegAsync()
    {
        const string ffmpegUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

        var tempZipPath = Path.Combine(Path.GetTempPath(), "ffmpeg.zip");
        var tempExtractPath = Path.Combine(Path.GetTempPath(), "ffmpeg_extract");

        try
        {
            using var response = await _httpClient.GetAsync(ffmpegUrl);
            response.EnsureSuccessStatusCode();

            await using (var fileStream = File.Create(tempZipPath))
            {
                await response.Content.CopyToAsync(fileStream);
            }

            if (Directory.Exists(tempExtractPath))
                Directory.Delete(tempExtractPath, true);

            ZipFile.ExtractToDirectory(tempZipPath, tempExtractPath);

            var ffmpegExe = Directory.GetFiles(tempExtractPath, "ffmpeg.exe", SearchOption.AllDirectories);
            if (ffmpegExe.Length > 0)
            {
                File.Copy(ffmpegExe[0], FfmpegPath, overwrite: true);
            }

            var ffprobeExe = Directory.GetFiles(tempExtractPath, "ffprobe.exe", SearchOption.AllDirectories);
            if (ffprobeExe.Length > 0)
            {
                File.Copy(ffprobeExe[0], Path.Combine(_appDirectory, "ffprobe.exe"), overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tempZipPath))
                File.Delete(tempZipPath);
            if (Directory.Exists(tempExtractPath))
                Directory.Delete(tempExtractPath, true);
        }
    }
}
