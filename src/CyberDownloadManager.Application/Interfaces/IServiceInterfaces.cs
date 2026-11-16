using CyberDownloadManager.Application.DTOs;
using CyberDownloadManager.Shared.Results;

namespace CyberDownloadManager.Application.Interfaces;

public interface IDownloadService
{
    Task<Result<IEnumerable<DownloadItemDto>>> GetActiveDownloadsAsync(CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<DownloadItemDto>>> GetCompletedDownloadsAsync(CancellationToken cancellationToken = default);
    Task RecoverInterruptedDownloadsAsync(CancellationToken cancellationToken = default);
    Task<Result<Guid>> AddDownloadAsync(string url, string destinationPath, CancellationToken cancellationToken = default);
    Task<Result<Guid>> AddDownloadToQueueAsync(string url, string destinationPath, CancellationToken cancellationToken = default);
    Task<Result> RenameCompletedDownloadAsync(Guid id, string newFileName, CancellationToken cancellationToken = default);
    Task<Result> PauseDownloadAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> ResumeDownloadAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> DeleteDownloadAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result> PauseAllAsync(CancellationToken cancellationToken = default);
    Task<Result> ResumeAllAsync(CancellationToken cancellationToken = default);
}

public interface IQueueService
{
    Task<Result> AddToQueueAsync(Guid downloadId, CancellationToken cancellationToken = default);
    Task<Result> ProcessNextAsync(CancellationToken cancellationToken = default);
}

public interface ISchedulerService
{
    Task<Result> ScheduleDownloadAsync(Guid downloadId, DateTime scheduledTime, CancellationToken cancellationToken = default);
}

public interface ISettingsService
{
    Task<T> GetSettingAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default);
    Task SetSettingAsync<T>(string key, T value, CancellationToken cancellationToken = default);
}

public interface ISettingsRepository
{
    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);
    Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    Task SendNotificationAsync(string title, string message, CancellationToken cancellationToken = default);
}
