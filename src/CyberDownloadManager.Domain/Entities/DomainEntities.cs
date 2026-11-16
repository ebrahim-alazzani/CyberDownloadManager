using CyberDownloadManager.Domain.Enums;

namespace CyberDownloadManager.Domain.Entities;

public class DownloadSegment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DownloadItemId { get; set; }
    public long StartByte { get; set; }
    public long EndByte { get; set; }
    public long DownloadedBytes { get; set; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Pending;
}

public class DownloadCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DownloadCategoryType CategoryType { get; set; } = DownloadCategoryType.Other;
    public string IconName { get; set; } = "folder";
}

public class DownloadSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DownloadItemId { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string RepeatType { get; set; } = "Once";
    public ScheduleStatus Status { get; set; } = ScheduleStatus.Pending;
}

public class DownloadHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DownloadItemId { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Details { get; set; }
}

public class ApplicationSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string DataType { get; set; } = "String";
}
