using CyberDownloadManager.Infrastructure.Persistence;
using CyberDownloadManager.Infrastructure.Repositories;
using CyberDownloadManager.Application.Services;
using CyberDownloadManager.Domain.Entities;
using CyberDownloadManager.Domain.Enums;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CyberDownloadManager.Infrastructure.Tests;

public class RepositoryTests
{
    [Fact]
    public void AppDbContext_CreateConnection_ShouldReturnValidConnection()
    {
        var context = new AppDbContext("Data Source=:memory:");
        using var connection = context.CreateConnection();

        Assert.NotNull(connection);
    }

    [Fact]
    public async Task Repository_ShouldPersistAndUpdateDownloadAcrossConnections()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cdm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var context = new AppDbContext(Path.Combine(directory, "downloads.db"));
            await new DatabaseInitializer(context).InitializeAsync();
            var repository = new DownloadRepository(context);
            var item = new DownloadItem
            {
                Url = "https://example.com/file.zip",
                FileName = "file.zip",
                FilePath = Path.Combine(directory, "file.zip"),
                Status = DownloadStatus.Downloading
            };

            await repository.AddAsync(item);
            item.DownloadedBytes = 128;
            item.Status = DownloadStatus.Paused;
            await repository.UpdateAsync(item);

            var loaded = await repository.GetByIdAsync(item.Id);
            Assert.NotNull(loaded);
            Assert.Equal(128, loaded.DownloadedBytes);
            Assert.Equal(DownloadStatus.Paused, loaded.Status);

            await repository.DeleteAsync(item.Id);
            Assert.Null(await repository.GetByIdAsync(item.Id));

            var settings = new SettingsService(new SettingsRepository(context));
            await settings.SetSettingAsync("downloads.defaultPath", "C:/Downloads/CDM");
            await settings.SetSettingAsync("downloads.connectionsPerTask", 32);
            var reopenedSettings = new SettingsService(new SettingsRepository(context));
            Assert.Equal("C:/Downloads/CDM", await reopenedSettings.GetSettingAsync("downloads.defaultPath", string.Empty));
            Assert.Equal(32, await reopenedSettings.GetSettingAsync("downloads.connectionsPerTask", 16));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
