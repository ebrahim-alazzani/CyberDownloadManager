namespace CyberDownloadManager.Application.Interfaces;

public record VideoInfo(
    string Title,
    string Url,
    string ThumbnailUrl,
    long DurationSeconds,
    IReadOnlyList<VideoFormat> Formats
);

public record VideoFormat(
    string FormatId,
    string Quality,       // e.g. "1080p", "720p", "480p"
    string Extension,     // e.g. "mp4", "webm"
    long? FileSizeBytes
);

public record PlaylistEntry(
    string Title,
    string Url,
    long DurationSeconds
);

public record PlaylistInfo(
    string Title,
    string PlaylistUrl,
    IReadOnlyList<PlaylistEntry> Entries
);

public interface IVideoDownloadService
{
    /// <summary>
    /// Checks if the given URL is a supported video platform (YouTube, Facebook, Twitter, etc.)
    /// </summary>
    bool IsVideoUrl(string url);

    /// <summary>
    /// Checks if the given URL is a playlist (e.g. YouTube playlist).
    /// </summary>
    bool IsPlaylistUrl(string url);

    /// <summary>
    /// Extracts video metadata (title, available formats/qualities, thumbnail).
    /// </summary>
    Task<VideoInfo?> GetVideoInfoAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts playlist metadata (title, list of video entries with titles and URLs).
    /// </summary>
    Task<PlaylistInfo?> GetPlaylistInfoAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a video with the specified quality to the given save path.
    /// </summary>
    Task<string> DownloadVideoAsync(string url, string quality, string savePath,
        IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}
