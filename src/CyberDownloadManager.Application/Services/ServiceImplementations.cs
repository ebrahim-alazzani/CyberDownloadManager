using CyberDownloadManager.Application.DTOs;
using CyberDownloadManager.Application.Interfaces;
using CyberDownloadManager.Domain.Entities;
using CyberDownloadManager.Domain.Enums;
using CyberDownloadManager.Domain.Interfaces;
using CyberDownloadManager.Shared.Results;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;

namespace CyberDownloadManager.Application.Services;

public class DownloadService : IDownloadService
{
    private readonly IDownloadRepository _repository;
    private readonly IDownloadEngine? _engine;
    private readonly ISettingsService? _settingsService;
    private readonly IVideoDownloadService? _videoDownloadService;
    private readonly ConcurrentDictionary<Guid, byte> _activeJobs = new();
    private readonly ConcurrentDictionary<Guid, Task> _jobTasks = new();
    private readonly ConcurrentDictionary<Guid, double> _currentSpeeds = new();

    public DownloadService(IDownloadRepository repository, IDownloadEngine? engine = null,
        ISettingsService? settingsService = null, IVideoDownloadService? videoDownloadService = null)
    {
        _repository = repository;
        _engine = engine;
        _settingsService = settingsService;
        _videoDownloadService = videoDownloadService;
    }

    public async Task<Result<IEnumerable<DownloadItemDto>>> GetActiveDownloadsAsync(CancellationToken cancellationToken = default)
    {
        var items = await _repository.GetAllAsync(cancellationToken);
        return Result<IEnumerable<DownloadItemDto>>.Success(
            items.Where(x => x.Status is DownloadStatus.Pending or DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Paused or DownloadStatus.Failed)
                 .OrderByDescending(x => x.CreatedAt)
                 .Select(MapToDto));
    }

    public async Task<Result<IEnumerable<DownloadItemDto>>> GetCompletedDownloadsAsync(CancellationToken cancellationToken = default)
    {
        var items = await _repository.GetAllAsync(cancellationToken);
        return Result<IEnumerable<DownloadItemDto>>.Success(
            items.Where(x => x.Status == DownloadStatus.Completed)
                 .Select(MapToDto));
    }

    public async Task RecoverInterruptedDownloadsAsync(CancellationToken cancellationToken = default)
    {
        var items = await _repository.GetAllAsync(cancellationToken);
        foreach (var item in items.Where(x => x.Status == DownloadStatus.Downloading))
        {
            item.Status = DownloadStatus.Failed;
            item.ErrorMessage = "The previous download was interrupted when the app closed. Select Resume to try again.";
            await _repository.UpdateAsync(item, cancellationToken);
        }
    }

    private DownloadItemDto MapToDto(DownloadItem item) => new()
    {
        Id = item.Id,
        Url = item.Url,
        FileName = item.FileName,
        FilePath = item.FilePath,
        ErrorMessage = item.ErrorMessage ?? (item.Status == DownloadStatus.Failed
            ? "No details were saved for the previous failure. Select Resume to try again."
            : null),
        FileSize = item.FileSize,
        DownloadedBytes = item.DownloadedBytes,
        Status = item.Status,
        Priority = item.Priority,
        CreatedAt = item.CreatedAt,
        CompletedAt = item.CompletedAt,
        SpeedBytesPerSecond = _currentSpeeds.GetValueOrDefault(item.Id)
    };

    public Task<Result<Guid>> AddDownloadAsync(string url, string destinationPath, CancellationToken cancellationToken = default) =>
        CreateDownloadAsync(url, destinationPath, DownloadStatus.Downloading, cancellationToken);

    public Task<Result<Guid>> AddDownloadToQueueAsync(string url, string destinationPath, CancellationToken cancellationToken = default) =>
        CreateDownloadAsync(url, destinationPath, DownloadStatus.Queued, cancellationToken);

    public async Task<Result> RenameCompletedDownloadAsync(Guid id, string newFileName,
        CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetByIdAsync(id, cancellationToken);
        if (item == null)
            return Result.Failure(new Error("Download.NotFound", "Download not found."));
        if (item.Status != DownloadStatus.Completed)
            return Result.Failure(new Error("Download.NotCompleted", "Only completed downloads can be renamed."));
        if (string.IsNullOrWhiteSpace(newFileName))
            return Result.Failure(new Error("Download.InvalidFileName", "Enter a file name."));

        var directory = Path.GetDirectoryName(item.FilePath);
        if (string.IsNullOrWhiteSpace(directory) || !File.Exists(item.FilePath))
            return Result.Failure(new Error("Download.FileMissing", "The downloaded file could not be found."));

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(newFileName.Trim());
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = new string(nameWithoutExtension
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray()).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(safeName))
            return Result.Failure(new Error("Download.InvalidFileName", "Enter a valid file name."));

        var extension = Path.GetExtension(item.FilePath);
        var newPath = Path.Combine(directory, safeName + extension);
        if (string.Equals(Path.GetFullPath(item.FilePath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            return Result.Success();
        if (File.Exists(newPath))
            return Result.Failure(new Error("Download.FileAlreadyExists", "A file with that name already exists."));

        var oldPath = item.FilePath;
        var oldName = item.FileName;
        try
        {
            File.Move(oldPath, newPath);
            item.FilePath = newPath;
            item.FileName = Path.GetFileName(newPath);
            await _repository.UpdateAsync(item, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            item.FilePath = oldPath;
            item.FileName = oldName;
            if (File.Exists(newPath) && !File.Exists(oldPath))
            {
                try { File.Move(newPath, oldPath); }
                catch (IOException) { }
            }
            return Result.Failure(new Error("Download.RenameFailed", ex.Message));
        }
    }

    private async Task<Result<Guid>> CreateDownloadAsync(string url, string destinationPath,
        DownloadStatus initialStatus, CancellationToken cancellationToken)
    {
        url = url.Trim();
        if (url.Contains("://", StringComparison.Ordinal) &&
            !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return Result.Failure<Guid>(new Error("Download.InvalidUrl", "Only HTTP and HTTPS URLs are supported."));
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Result.Failure<Guid>(new Error("Download.InvalidUrl", "Enter a valid HTTP or HTTPS URL."));
        if (string.IsNullOrWhiteSpace(destinationPath))
            return Result.Failure<Guid>(new Error("Download.InvalidPath", "Choose a destination folder."));

        var fileName = Path.GetFileName(Uri.UnescapeDataString(uri.LocalPath));
        if (string.IsNullOrWhiteSpace(fileName)) fileName = $"download_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(fileName)) fileName = $"download_{Guid.NewGuid():N}";
        string filePath;
        try
        {
            Directory.CreateDirectory(destinationPath);
            filePath = GetAvailableFilePath(destinationPath, fileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Result.Failure<Guid>(new Error("Download.InvalidPath", ex.Message));
        }

        var item = new DownloadItem
        {
            Id = Guid.NewGuid(),
            Url = url,
            FileName = fileName,
            FilePath = filePath,
            Status = initialStatus,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(item, cancellationToken);
        if (_engine != null && initialStatus == DownloadStatus.Downloading) StartInBackground(item);
        return Result.Success(item.Id);
    }

    public async Task<Result> PauseDownloadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetByIdAsync(id, cancellationToken);
        if (item == null) return Result.Failure(new Error("Download.NotFound", "Download not found."));

        item.Status = DownloadStatus.Paused;
        await _repository.UpdateAsync(item, cancellationToken);
        if (_engine != null && _activeJobs.ContainsKey(id))
        {
            await _engine.PauseAsync(id, cancellationToken);
            if (_jobTasks.TryGetValue(id, out var job)) await job.WaitAsync(cancellationToken);
            _jobTasks.TryRemove(id, out var stoppedTask);
            _activeJobs.TryRemove(id, out var stoppedMarker);
        }

        return Result.Success();
    }

    public async Task<Result> ResumeDownloadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetByIdAsync(id, cancellationToken);
        if (item == null) return Result.Failure(new Error("Download.NotFound", "Download not found."));

        if (item.Status == DownloadStatus.Completed)
            return Result.Failure(new Error("Download.AlreadyCompleted", "A completed download cannot be resumed."));
        item.Status = DownloadStatus.Downloading;
        item.ErrorMessage = null;
        await _repository.UpdateAsync(item, cancellationToken);
        if (_engine != null) StartInBackground(item);
        return Result.Success();
    }

    public async Task<Result> DeleteDownloadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetByIdAsync(id, cancellationToken);
        if (item == null) return Result.Failure(new Error("Download.NotFound", "Download not found."));

        item.Status = DownloadStatus.Cancelled;
        await _repository.UpdateAsync(item, cancellationToken);
        if (_engine != null && _activeJobs.ContainsKey(id))
        {
            await _engine.CancelAsync(id, cancellationToken);
            if (_jobTasks.TryGetValue(id, out var job)) await job.WaitAsync(cancellationToken);
            _jobTasks.TryRemove(id, out var stoppedTask);
            _activeJobs.TryRemove(id, out var stoppedMarker);
        }

        await _repository.DeleteAsync(id, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> PauseAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await _repository.GetAllAsync(cancellationToken);
        foreach (var item in items.Where(x => x.Status is DownloadStatus.Downloading or DownloadStatus.Pending or DownloadStatus.Queued))
        {
            await PauseDownloadAsync(item.Id, cancellationToken);
        }
        return Result.Success();
    }

    public async Task<Result> ResumeAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await _repository.GetAllAsync(cancellationToken);
        foreach (var item in items.Where(x => x.Status == DownloadStatus.Paused))
            await ResumeDownloadAsync(item.Id, cancellationToken);
        return Result.Success();
    }

    private void StartInBackground(DownloadItem item)
    {
        if (!_activeJobs.TryAdd(item.Id, 0)) return;
        var task = Task.Run(async () =>
        {
            await Task.Yield();
            await RunDownloadAsync(item);
        });
        _jobTasks[item.Id] = task;
        _ = task.ContinueWith(completedTask =>
        {
            _jobTasks.TryRemove(item.Id, out var removedTask);
            _activeJobs.TryRemove(item.Id, out var removedMarker);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task RunDownloadAsync(DownloadItem item)
    {
        using var writeLock = new SemaphoreSlim(1, 1);
        var lastWrite = Stopwatch.StartNew();
        var progress = new Progress<DownloadProgress>(p =>
        {
            item.FileSize = p.TotalBytes;
            item.DownloadedBytes = p.DownloadedBytes;
            _currentSpeeds[item.Id] = p.SpeedBytesPerSecond;
            if (lastWrite.ElapsedMilliseconds >= 500)
            {
                lastWrite.Restart();
                _ = PersistProgressAsync(item, writeLock);
            }
        });

        try
        {
            var connectionCount = _settingsService is null
                ? 16
                : await _settingsService.GetSettingAsync("downloads.connectionsPerTask", 16);
            var result = _videoDownloadService?.IsVideoUrl(item.Url) == true
                ? await DownloadVideoAsync(item, progress)
                : await _engine!.StartAsync(item.Id, item.Url, item.FilePath,
                    Math.Clamp(connectionCount, 1, 32), progress);
            if (result.IsSuccess)
            {
                item.Status = DownloadStatus.Completed;
                item.ErrorMessage = null;
                item.CompletedAt = DateTime.UtcNow;
                _currentSpeeds.TryRemove(item.Id, out var ignoredSpeed);
                if (File.Exists(item.FilePath))
                {
                    await using var file = File.OpenRead(item.FilePath);
                    var hash = await SHA256.HashDataAsync(file);
                    item.Checksum = Convert.ToHexString(hash);
                    item.DownloadedBytes = item.FileSize = file.Length;
                }
            }
            else if (item.Status != DownloadStatus.Paused && item.Status != DownloadStatus.Cancelled)
            {
                item.Status = DownloadStatus.Failed;
                item.ErrorMessage = result.Error.Description;
            }
        }
        catch (Exception ex)
        {
            if (item.Status != DownloadStatus.Paused && item.Status != DownloadStatus.Cancelled)
            {
                item.Status = DownloadStatus.Failed;
                item.ErrorMessage = ex.Message;
            }
            _currentSpeeds.TryRemove(item.Id, out var ignoredSpeed);
        }
        finally
        {
            _currentSpeeds.TryRemove(item.Id, out var ignoredSpeed);
            await PersistProgressAsync(item, writeLock);
        }
    }

    private async Task<Result> DownloadVideoAsync(DownloadItem item, IProgress<DownloadProgress> progress)
    {
        item.FilePath = await _videoDownloadService!.DownloadVideoAsync(item.Url, "1080p", item.FilePath, progress);
        item.FileName = Path.GetFileName(item.FilePath);
        if (!File.Exists(item.FilePath))
            return Result.Failure(new Error("Download.VideoFileMissing", "The video provider finished without creating the output file."));
        return Result.Success();
    }

    private async Task PersistProgressAsync(DownloadItem item, SemaphoreSlim writeLock)
    {
        await writeLock.WaitAsync();
        try { await _repository.UpdateAsync(item); }
        catch { /* The final write below retries persistence when the job ends. */ }
        finally { writeLock.Release(); }
    }

    private static string GetAvailableFilePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (IsPathAvailable(path)) return path;
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 1; ; index++)
        {
            path = Path.Combine(directory, $"{name} ({index}){extension}");
            if (IsPathAvailable(path)) return path;
        }
    }

    private static bool IsPathAvailable(string path) => !File.Exists(path) &&
        !File.Exists(path + ".download") && !File.Exists(path + ".part0");
}

public class QueueService : IQueueService
{
    private readonly IDownloadRepository _repository;
    private readonly IDownloadService _downloadService;

    public QueueService(IDownloadRepository repository, IDownloadService downloadService)
    {
        _repository = repository;
        _downloadService = downloadService;
    }

    public async Task<Result> AddToQueueAsync(Guid downloadId, CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetByIdAsync(downloadId, cancellationToken);
        if (item == null) return Result.Failure(new Error("Download.NotFound", "Download not found."));

        item.Status = DownloadStatus.Queued;
        await _repository.UpdateAsync(item, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var all = await _repository.GetAllAsync(cancellationToken);
        var pending = all.FirstOrDefault(x => x.Status is DownloadStatus.Pending or DownloadStatus.Queued);
        if (pending == null) return Result.Success();

        return await _downloadService.ResumeDownloadAsync(pending.Id, cancellationToken);
    }
}

public class SchedulerService : ISchedulerService
{
    private readonly IDownloadRepository _repository;

    public SchedulerService(IDownloadRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result> ScheduleDownloadAsync(Guid downloadId, DateTime scheduledTime, CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetByIdAsync(downloadId, cancellationToken);
        if (item == null) return Result.Failure(new Error("Download.NotFound", "Download not found."));

        item.Status = DownloadStatus.Pending;
        await _repository.UpdateAsync(item, cancellationToken);
        return Result.Success();
    }
}

public class SettingsService : ISettingsService
{
    private readonly ISettingsRepository _repository;

    public SettingsService(ISettingsRepository repository) => _repository = repository;

    public async Task<T> GetSettingAsync<T>(string key, T defaultValue, CancellationToken cancellationToken = default)
    {
        var value = await _repository.GetValueAsync(key, cancellationToken);
        if (!string.IsNullOrWhiteSpace(value))
        {
            try
            {
                var setting = JsonSerializer.Deserialize<T>(value);
                if (setting is not null) return setting;
            }
            catch (JsonException) { }
        }
        return defaultValue;
    }

    public Task SetSettingAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        var serialized = JsonSerializer.Serialize(value);
        return _repository.SetValueAsync(key, serialized, cancellationToken);
    }
}

public class NotificationService : INotificationService
{
    public Task SendNotificationAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
