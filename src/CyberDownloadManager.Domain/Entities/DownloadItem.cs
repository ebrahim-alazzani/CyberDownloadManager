using CyberDownloadManager.Domain.Enums;

namespace CyberDownloadManager.Domain.Entities;

public class DownloadItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public long DownloadedBytes { get; set; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Pending;
    public DownloadPriority Priority { get; set; } = DownloadPriority.Normal;
    public Guid? CategoryId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? Checksum { get; set; }
    public string? ErrorMessage { get; set; }
    public double ProgressPercentage => FileSize > 0 ? (double)DownloadedBytes / FileSize * 100 : 0;
}
