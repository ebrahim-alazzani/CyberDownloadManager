namespace CyberDownloadManager.Domain.Enums;

public enum DownloadStatus
{
    Pending,
    Queued,
    Downloading,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public enum DownloadPriority
{
    Low,
    Normal,
    High
}

public enum DownloadCategoryType
{
    Software,
    Media,
    Documents,
    ISOs,
    Archives,
    Other
}

public enum ScheduleStatus
{
    Pending,
    Running,
    Completed,
    Cancelled
}
