using System.Collections.Concurrent;
using CyberDownloadManager.Application.Interfaces;
using CyberDownloadManager.Application.Services;
using CyberDownloadManager.Domain.Entities;
using CyberDownloadManager.Domain.Enums;
using CyberDownloadManager.Domain.Interfaces;
using CyberDownloadManager.Shared.Results;
using Xunit;

namespace CyberDownloadManager.Application.Tests;

public class ApplicationServiceTests
{
    [Fact]
    public async Task GetActiveDownloads_ShouldIncludeFailedItemsForRetryAndExcludeCompletedItems()
    {
        var repository = new FakeDownloadRepository(
            new DownloadItem { FileName = "active.iso", Status = DownloadStatus.Downloading },
            new DownloadItem { FileName = "queued.zip", Status = DownloadStatus.Queued },
            new DownloadItem { FileName = "done.bin", Status = DownloadStatus.Completed },
            new DownloadItem { FileName = "broken.bin", Status = DownloadStatus.Failed });
        var service = new DownloadService(repository);

        var result = await service.GetActiveDownloadsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "broken.bin", "queued.zip", "active.iso" }, result.Value.Select(x => x.FileName));
    }

    [Fact]
    public async Task AddDownload_ShouldRejectInvalidUrlWithoutCreatingAnItem()
    {
        var repository = new FakeDownloadRepository();
        var service = new DownloadService(repository);

        var result = await service.AddDownloadAsync("file:///C:/secret.txt", Path.GetTempPath());

        Assert.True(result.IsFailure);
        Assert.Empty(repository.Items);
    }

    [Fact]
    public async Task AddDownload_ShouldPersistAndRunTheDownload()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"cdm-{Guid.NewGuid():N}");
        var repository = new FakeDownloadRepository();
        var engine = new SuccessfulDownloadEngine();
        var service = new DownloadService(repository, engine);
        try
        {
            var result = await service.AddDownloadAsync("https://example.test/file.bin", folder);

            Assert.True(result.IsSuccess);
            await WaitUntilAsync(() => repository.Items.TryGetValue(result.Value, out var item) &&
                item.Status == DownloadStatus.Completed);
            Assert.Equal(1, engine.StartCount);
            Assert.Equal(DownloadStatus.Completed, repository.Items[result.Value].Status);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ResumeDownload_ShouldRetryFailedVideoThroughVideoService()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"cdm-video-retry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "video.mp4");
        var item = new DownloadItem
        {
            Url = "https://www.youtube.com/watch?v=example",
            FileName = Path.GetFileName(path),
            FilePath = path,
            Status = DownloadStatus.Failed,
            ErrorMessage = "previous failure"
        };
        var repository = new FakeDownloadRepository(item);
        var videoService = new SuccessfulVideoDownloadService();
        var service = new DownloadService(repository, new SuccessfulDownloadEngine(), videoDownloadService: videoService);

        try
        {
            var result = await service.ResumeDownloadAsync(item.Id);

            Assert.True(result.IsSuccess);
            await WaitUntilAsync(() => repository.Items[item.Id].Status == DownloadStatus.Completed);
            Assert.Equal(1, videoService.DownloadCount);
            Assert.Null(repository.Items[item.Id].ErrorMessage);
            Assert.True(File.Exists(repository.Items[item.Id].FilePath));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task RenameCompletedDownload_ShouldRenameFileAndPersistNewPath()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"cdm-rename-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var oldPath = Path.Combine(folder, "old-name.mp4");
        await File.WriteAllTextAsync(oldPath, "video data");
        var item = new DownloadItem
        {
            FileName = Path.GetFileName(oldPath),
            FilePath = oldPath,
            Status = DownloadStatus.Completed
        };
        var repository = new FakeDownloadRepository(item);
        var service = new DownloadService(repository);

        try
        {
            var result = await service.RenameCompletedDownloadAsync(item.Id, "New video name");

            Assert.True(result.IsSuccess);
            Assert.False(File.Exists(oldPath));
            Assert.Equal("New video name.mp4", repository.Items[item.Id].FileName);
            Assert.True(File.Exists(repository.Items[item.Id].FilePath));
            Assert.Equal("video data", await File.ReadAllTextAsync(repository.Items[item.Id].FilePath));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task RenameCompletedDownload_ShouldKeepOriginalFileWhenNameAlreadyExists()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"cdm-rename-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var oldPath = Path.Combine(folder, "old-name.mp4");
        var conflictingPath = Path.Combine(folder, "taken.mp4");
        await File.WriteAllTextAsync(oldPath, "video data");
        await File.WriteAllTextAsync(conflictingPath, "other file");
        var item = new DownloadItem
        {
            FileName = Path.GetFileName(oldPath),
            FilePath = oldPath,
            Status = DownloadStatus.Completed
        };
        var repository = new FakeDownloadRepository(item);
        var service = new DownloadService(repository);

        try
        {
            var result = await service.RenameCompletedDownloadAsync(item.Id, "taken");

            Assert.True(result.IsFailure);
            Assert.True(File.Exists(oldPath));
            Assert.Equal(oldPath, repository.Items[item.Id].FilePath);
            Assert.Equal("video data", await File.ReadAllTextAsync(oldPath));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout) throw new TimeoutException("The background download did not finish.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeDownloadRepository(params DownloadItem[] items) : IDownloadRepository
    {
        public ConcurrentDictionary<Guid, DownloadItem> Items { get; } = new(items.ToDictionary(x => x.Id));

        public Task<DownloadItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.TryGetValue(id, out var item) ? item : null);

        public Task<IReadOnlyList<DownloadItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadItem>>(Items.Values.ToList());

        public Task<Guid> AddAsync(DownloadItem item, CancellationToken cancellationToken = default)
        {
            Items[item.Id] = item;
            return Task.FromResult(item.Id);
        }

        public Task UpdateAsync(DownloadItem item, CancellationToken cancellationToken = default)
        {
            Items[item.Id] = item;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Items.TryRemove(id, out _);
            return Task.CompletedTask;
        }
    }

    private sealed class SuccessfulDownloadEngine : IDownloadEngine
    {
        public int StartCount { get; private set; }

        public Task<Result> StartAsync(Guid downloadId, string url, string filePath, int segmentCount,
            IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            StartCount++;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> PauseAsync(Guid downloadId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> ResumeAsync(Guid downloadId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> CancelAsync(Guid downloadId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
    }

    private sealed class SuccessfulVideoDownloadService : IVideoDownloadService
    {
        public int DownloadCount { get; private set; }

        public bool IsVideoUrl(string url) => url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase);
        public bool IsPlaylistUrl(string url) => false;
        public Task<VideoInfo?> GetVideoInfoAsync(string url, CancellationToken cancellationToken = default) => Task.FromResult<VideoInfo?>(null);
        public Task<PlaylistInfo?> GetPlaylistInfoAsync(string url, CancellationToken cancellationToken = default) => Task.FromResult<PlaylistInfo?>(null);

        public async Task<string> DownloadVideoAsync(string url, string quality, string savePath,
            IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            DownloadCount++;
            await File.WriteAllTextAsync(savePath, "video bytes", cancellationToken);
            return savePath;
        }
    }
}
