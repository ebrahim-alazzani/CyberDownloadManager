using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CyberDownloadManager.Application.DTOs;
using CyberDownloadManager.Application.Interfaces;

namespace CyberDownloadManager.Presentation.ViewModels;

public partial class ActiveTransfersViewModel : ObservableObject
{
    private readonly IDownloadService _downloadService;

    [ObservableProperty]
    private ObservableCollection<DownloadItemDto> _transfers = new();

    public ActiveTransfersViewModel(IDownloadService downloadService)
    {
        _downloadService = downloadService;
    }
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private string _downloadPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CyberManager");

    [ObservableProperty]
    private int _maxConcurrentDownloads = 5;

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        DownloadPath = await _settingsService.GetSettingAsync(
            "downloads.defaultPath", DownloadPath, cancellationToken);
        MaxConcurrentDownloads = await _settingsService.GetSettingAsync(
            "downloads.connectionsPerTask", MaxConcurrentDownloads, cancellationToken);
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _settingsService.SetSettingAsync("downloads.defaultPath", DownloadPath, cancellationToken);
        await _settingsService.SetSettingAsync("downloads.connectionsPerTask",
            Math.Clamp(MaxConcurrentDownloads, 1, 32), cancellationToken);
    }
}
