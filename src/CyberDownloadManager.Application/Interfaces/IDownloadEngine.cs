using CyberDownloadManager.Shared.Results;

namespace CyberDownloadManager.Application.Interfaces;

/// <summary>
/// Progress data reported during a download operation.
/// </summary>
public record DownloadProgress(
    Guid DownloadId,
    long TotalBytes,
    long DownloadedBytes,
    double SpeedBytesPerSecond,
    double ProgressPercentage,
    TimeSpan EstimatedTimeRemaining
);

public interface IDownloadEngine
{
    Task<Result> StartAsync(Guid downloadId, string url, string filePath, int segmentCount,
        IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<Result> PauseAsync(Guid downloadId, CancellationToken cancellationToken = default);
    Task<Result> ResumeAsync(Guid downloadId, CancellationToken cancellationToken = default);
    Task<Result> CancelAsync(Guid downloadId, CancellationToken cancellationToken = default);
}
