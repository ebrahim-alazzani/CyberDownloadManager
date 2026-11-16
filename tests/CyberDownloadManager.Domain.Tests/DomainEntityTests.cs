using CyberDownloadManager.Domain.Entities;
using CyberDownloadManager.Domain.Enums;
using Xunit;

namespace CyberDownloadManager.Domain.Tests;

public class DomainEntityTests
{
    [Fact]
    public void DownloadItem_DefaultValues_ShouldBePendingAndNormalPriority()
    {
        var item = new DownloadItem();

        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal(DownloadStatus.Pending, item.Status);
        Assert.Equal(DownloadPriority.Normal, item.Priority);
        Assert.Equal(0, item.ProgressPercentage);
    }

    [Fact]
    public void DownloadItem_ProgressPercentage_ShouldCalculateCorrectly()
    {
        var item = new DownloadItem
        {
            FileSize = 1000,
            DownloadedBytes = 450
        };

        Assert.Equal(45.0, item.ProgressPercentage);
    }
}
