using CyberDownloadManager.Application.Interfaces;
using CyberDownloadManager.Infrastructure.Persistence;
using Dapper;

namespace CyberDownloadManager.Infrastructure.Repositories;

public sealed class SettingsRepository : ISettingsRepository
{
    private readonly AppDbContext _context;

    public SettingsRepository(AppDbContext context) => _context = context;

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        var command = new CommandDefinition(
            "SELECT Value FROM ApplicationSettings WHERE Key = @Key",
            new { Key = key }, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<string?>(command);
    }

    public async Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        using var connection = _context.CreateConnection();
        var command = new CommandDefinition(@"
            INSERT INTO ApplicationSettings (Key, Value, Category, DataType)
            VALUES (@Key, @Value, 'General', 'Json')
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value, DataType = excluded.DataType;",
            new { Key = key, Value = value }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
