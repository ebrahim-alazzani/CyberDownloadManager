using CyberDownloadManager.Domain.Entities;
using CyberDownloadManager.Domain.Enums;
using CyberDownloadManager.Domain.Interfaces;
using CyberDownloadManager.Infrastructure.Persistence;
using Dapper;

namespace CyberDownloadManager.Infrastructure.Repositories;

public class DownloadRepository : IDownloadRepository
{
    private readonly AppDbContext _context;

    public DownloadRepository(AppDbContext context)
    {
        _context = context;
    }

    private class DownloadItemDbModel
    {
        public string Id { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public long DownloadedBytes { get; set; }
        public int Status { get; set; }
        public int Priority { get; set; }
        public string? CategoryId { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string? CompletedAt { get; set; }
        public string? Checksum { get; set; }
        public string? ErrorMessage { get; set; }

        public DownloadItem ToDomain() => new DownloadItem
        {
            Id = Guid.TryParse(Id, out var g) ? g : Guid.NewGuid(),
            Url = Url,
            FileName = FileName,
            FilePath = FilePath,
            FileSize = FileSize,
            DownloadedBytes = DownloadedBytes,
            Status = (DownloadStatus)Status,
            Priority = (DownloadPriority)Priority,
            CategoryId = Guid.TryParse(CategoryId, out var cg) ? cg : null,
            CreatedAt = DateTime.TryParse(CreatedAt, out var dt) ? dt : DateTime.UtcNow,
            CompletedAt = DateTime.TryParse(CompletedAt, out var cdt) ? cdt : null,
            Checksum = Checksum,
            ErrorMessage = ErrorMessage
        };
    }

    public async Task<DownloadItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        const string sql = "SELECT * FROM Downloads WHERE Id = @Id";
        var command = new CommandDefinition(sql, new { Id = id.ToString() }, cancellationToken: cancellationToken);
        var dbModel = await connection.QueryFirstOrDefaultAsync<DownloadItemDbModel>(command);
        return dbModel?.ToDomain();
    }

    public async Task<IReadOnlyList<DownloadItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        const string sql = "SELECT * FROM Downloads ORDER BY CreatedAt DESC";
        var command = new CommandDefinition(sql, cancellationToken: cancellationToken);
        var dbModels = await connection.QueryAsync<DownloadItemDbModel>(command);
        return dbModels.Select(x => x.ToDomain()).ToList().AsReadOnly();
    }

    public async Task<Guid> AddAsync(DownloadItem item, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        const string sql = @"
            INSERT INTO Downloads (Id, Url, FileName, FilePath, FileSize, DownloadedBytes, Status, Priority, CategoryId, CreatedAt, CompletedAt, Checksum, ErrorMessage)
            VALUES (@Id, @Url, @FileName, @FilePath, @FileSize, @DownloadedBytes, @Status, @Priority, @CategoryId, @CreatedAt, @CompletedAt, @Checksum, @ErrorMessage);";

        var command = new CommandDefinition(sql, new
        {
            Id = item.Id.ToString(),
            item.Url,
            item.FileName,
            item.FilePath,
            item.FileSize,
            item.DownloadedBytes,
            Status = (int)item.Status,
            Priority = (int)item.Priority,
            CategoryId = item.CategoryId?.ToString(),
            CreatedAt = item.CreatedAt.ToString("o"),
            CompletedAt = item.CompletedAt?.ToString("o"),
            item.Checksum,
            item.ErrorMessage
        }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);

        return item.Id;
    }

    public async Task UpdateAsync(DownloadItem item, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        const string sql = @"
            UPDATE Downloads 
            SET Url = @Url, FileName = @FileName, FilePath = @FilePath, FileSize = @FileSize, 
                DownloadedBytes = @DownloadedBytes, Status = @Status, Priority = @Priority, 
                CompletedAt = @CompletedAt, Checksum = @Checksum, ErrorMessage = @ErrorMessage
            WHERE Id = @Id;";

        var command = new CommandDefinition(sql, new
        {
            Id = item.Id.ToString(),
            item.Url,
            item.FileName,
            item.FilePath,
            item.FileSize,
            item.DownloadedBytes,
            Status = (int)item.Status,
            Priority = (int)item.Priority,
            CompletedAt = item.CompletedAt?.ToString("o"),
            item.Checksum,
            item.ErrorMessage
        }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        const string sql = "DELETE FROM Downloads WHERE Id = @Id";
        var command = new CommandDefinition(sql, new { Id = id.ToString() }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
