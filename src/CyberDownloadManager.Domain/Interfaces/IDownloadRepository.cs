using CyberDownloadManager.Domain.Entities;

namespace CyberDownloadManager.Domain.Interfaces;

public interface IDownloadRepository
{
    Task<DownloadItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DownloadItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Guid> AddAsync(DownloadItem item, CancellationToken cancellationToken = default);
    Task UpdateAsync(DownloadItem item, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
