using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CyberDownloadManager.Application.DTOs;
using CyberDownloadManager.Application.Interfaces;

namespace CyberDownloadManager.Presentation.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IDownloadService _downloadService;

    [ObservableProperty]
    private ObservableCollection<DownloadItemDto> _activeDownloads = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _globalSpeed = "0 B/s";

    [ObservableProperty]
    private string _completedTodayText = "0 Files (0 B)";

    [ObservableProperty]
    private string _storageRemainingText = "Calculating...";

    [ObservableProperty]
    private string _activeStreamsText = "0 Tasks (0 Threads)";

    [ObservableProperty]
    private Visibility _activeListVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility _emptyStateVisibility = Visibility.Visible;

    public DashboardViewModel(IDownloadService downloadService)
    {
        _downloadService = downloadService;
        UpdateStorageInfo();
    }

    [RelayCommand]
    public async Task LoadDownloadsAsync()
    {
        IsLoading = true;
        try
        {
            var result = await _downloadService.GetActiveDownloadsAsync();
            if (result.IsSuccess)
            {
                var freshItems = result.Value.ToList();
                var freshIds = new HashSet<Guid>(freshItems.Select(x => x.Id));

                // Remove items that no longer exist in DB
                for (int i = ActiveDownloads.Count - 1; i >= 0; i--)
                {
                    if (!freshIds.Contains(ActiveDownloads[i].Id))
                        ActiveDownloads.RemoveAt(i);
                }

                // Update existing or add new items
                foreach (var fresh in freshItems)
                {
                    var existing = ActiveDownloads.FirstOrDefault(x => x.Id == fresh.Id);
                    if (existing != null)
                    {
                        // Update in-place (triggers PropertyChanged via ObservableObject)
                        existing.DownloadedBytes = fresh.DownloadedBytes;
                        existing.FileSize = fresh.FileSize;
                        existing.Status = fresh.Status;
                        existing.SpeedBytesPerSecond = fresh.SpeedBytesPerSecond;
                    }
                    else
                    {
                        ActiveDownloads.Add(fresh);
                    }
                }

                var activeCount = ActiveDownloads.Count(x => x.Status == Domain.Enums.DownloadStatus.Downloading);
                ActiveStreamsText = $"{ActiveDownloads.Count} Tasks ({activeCount * 16} Threads)";
                GlobalSpeed = FormatSpeed(ActiveDownloads
                    .Where(x => x.Status == Domain.Enums.DownloadStatus.Downloading)
                    .Sum(x => x.SpeedBytesPerSecond));
                ActiveListVisibility = ActiveDownloads.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                EmptyStateVisibility = ActiveDownloads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            var completed = await _downloadService.GetCompletedDownloadsAsync();
            if (completed.IsSuccess)
            {
                var today = completed.Value
                    .Where(x => x.CompletedAt?.ToUniversalTime().Date == DateTime.UtcNow.Date)
                    .ToList();
                CompletedTodayText = $"{today.Count} Files ({FormatBytes(today.Sum(x => x.FileSize))})";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string FormatSpeed(double bytesPerSecond) => $"{FormatBytes((long)bytesPerSecond)}/s";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = Math.Max(0, (double)bytes);
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }

    private void UpdateStorageInfo()
    {
        try
        {
            var drive = new System.IO.DriveInfo("C");
            var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
            StorageRemainingText = $"{freeGb:F2} GB Free (Drive {drive.Name})";
        }
        catch
        {
            StorageRemainingText = "N/A";
        }
    }
}
