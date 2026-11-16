using System.ComponentModel;
using System.Runtime.CompilerServices;
using CyberDownloadManager.Domain.Enums;

namespace CyberDownloadManager.Application.DTOs;

public class DownloadItemDto : INotifyPropertyChanged
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime? CompletedAt { get; set; }
    private double _speedBytesPerSecond;
    public double SpeedBytesPerSecond
    {
        get => _speedBytesPerSecond;
        set
        {
            if (_speedBytesPerSecond != value)
            {
                _speedBytesPerSecond = value;
                Notify();
                Notify(nameof(SpeedText));
            }
        }
    }
    public string SpeedText => FormatBytes((long)SpeedBytesPerSecond) + "/s";

    private long _fileSize;
    public long FileSize
    {
        get => _fileSize;
        set
        {
            if (_fileSize != value)
            {
                _fileSize = value;
                Notify();
                Notify(nameof(ProgressPercentage));
                Notify(nameof(FileSizeText));
                Notify(nameof(ProgressText));
            }
        }
    }

    private long _downloadedBytes;
    public long DownloadedBytes
    {
        get => _downloadedBytes;
        set
        {
            if (_downloadedBytes != value)
            {
                _downloadedBytes = value;
                Notify();
                Notify(nameof(ProgressPercentage));
                Notify(nameof(DownloadedBytesText));
                Notify(nameof(ProgressText));
            }
        }
    }

    private DownloadStatus _status;
    public DownloadStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                Notify();
                Notify(nameof(StatusText));
            }
        }
    }

    public DownloadPriority Priority { get; set; }
    public DateTime CreatedAt { get; set; }

    // Computed display properties
    public double ProgressPercentage => FileSize > 0 ? (double)DownloadedBytes / FileSize * 100 : 0;

    public string FileSizeText => FormatBytes(FileSize);
    public string DownloadedBytesText => FormatBytes(DownloadedBytes);

    public string ProgressText => Status == DownloadStatus.Failed
        ? "Failed"
        : Status == DownloadStatus.Paused
            ? "Paused"
            : FileSize > 0
                ? $"{DownloadedBytesText} / {FileSizeText}  ({ProgressPercentage:F1}%)"
                : DownloadedBytes > 0
                    ? $"{DownloadedBytesText} (unknown size)"
                    : "Connecting...";

    public string StatusText => Status switch
    {
        DownloadStatus.Downloading => "⬇ Downloading",
        DownloadStatus.Paused => "⏸ Paused",
        DownloadStatus.Completed => "✅ Completed",
        DownloadStatus.Failed => "❌ Failed",
        DownloadStatus.Cancelled => "Cancelled",
        DownloadStatus.Pending => "⏳ Queued",
        DownloadStatus.Queued => "⏳ Queued",
        _ => Status.ToString()
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double size = bytes;
        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return $"{size:F2} {suffixes[i]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
