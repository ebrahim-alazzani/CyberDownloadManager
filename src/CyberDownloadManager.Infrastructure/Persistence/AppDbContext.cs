using System.Data;
using Microsoft.Data.Sqlite;

namespace CyberDownloadManager.Infrastructure.Persistence;

public class AppDbContext
{
    private readonly string _connectionString;

    public AppDbContext(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    public IDbConnection CreateConnection() => new SqliteConnection(_connectionString);
}
