using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;

namespace YouTubeDownloader.Models;

public enum AudioFormat
{
    MP3,
    FLAC,
    Opus,
    AAC,
    WAV
}

public partial class DownloadSettings : ObservableObject
{
    [ObservableProperty]
    private string _outputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "YouTube Downloads");

    [ObservableProperty]
    private int _audioBitrate = 320;

    [ObservableProperty]
    private AudioFormat _audioFormat = AudioFormat.MP3;

    [ObservableProperty]
    private bool _downloadPlaylist = true;

    [ObservableProperty]
    private bool _embedMetadata = true;

    [ObservableProperty]
    private bool _embedThumbnail = true;

    [ObservableProperty]
    private int _maxConcurrentDownloads = 3;

    [ObservableProperty]
    private string _fileNamePattern = "%(artist)s - %(title)s.%(ext)s";

    [ObservableProperty]
    private bool _useArtistTitleNaming = true;

    public static int[] AvailableBitrates => [128, 192, 256, 320];
    public static AudioFormat[] AvailableFormats => [AudioFormat.MP3, AudioFormat.FLAC, AudioFormat.Opus, AudioFormat.AAC, AudioFormat.WAV];
    public static int[] AvailableConcurrentDownloads => [1, 2, 3, 4, 5];

    public string GetFileExtension() => AudioFormat switch
    {
        AudioFormat.MP3 => "mp3",
        AudioFormat.FLAC => "flac",
        AudioFormat.Opus => "opus",
        AudioFormat.AAC => "m4a",
        AudioFormat.WAV => "wav",
        _ => "mp3"
    };

    public string GetYtDlpFormat() => AudioFormat switch
    {
        AudioFormat.MP3 => "mp3",
        AudioFormat.FLAC => "flac",
        AudioFormat.Opus => "opus",
        AudioFormat.AAC => "m4a",
        AudioFormat.WAV => "wav",
        _ => "mp3"
    };

    public string GetOutputTemplate(string outputFolder)
    {
        if (UseArtistTitleNaming)
        {
            // Use artist - track format. %(track)s is the clean song name without artist prefix.
            // Falls back to title if track not available.
            return Path.Combine(outputFolder, "%(artist,uploader)s - %(track,title)s.%(ext)s");
        }
        return Path.Combine(outputFolder, "%(title)s.%(ext)s");
    }
}
