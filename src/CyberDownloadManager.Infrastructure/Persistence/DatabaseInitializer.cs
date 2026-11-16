using Dapper;

namespace CyberDownloadManager.Infrastructure.Persistence;

public class DatabaseInitializer
{
    private readonly AppDbContext _context;

    public DatabaseInitializer(AppDbContext context)
    {
        _context = context;
    }

    public async Task InitializeAsync()
    {
        using var connection = _context.CreateConnection();

        const string createSchemaVersionTableSql = @"
            CREATE TABLE IF NOT EXISTS SchemaVersion (
                Version INTEGER PRIMARY KEY,
                AppliedAt TEXT NOT NULL
            );";

        await connection.ExecuteAsync(createSchemaVersionTableSql);

        const string createDownloadsTableSql = @"
            CREATE TABLE IF NOT EXISTS Downloads (
                Id TEXT PRIMARY KEY,
                Url TEXT NOT NULL,
                FileName TEXT NOT NULL,
                FilePath TEXT NOT NULL,
                FileSize INTEGER NOT NULL,
                DownloadedBytes INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                Priority INTEGER NOT NULL,
                CategoryId TEXT NULL,
                CreatedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                Checksum TEXT NULL,
                ErrorMessage TEXT NULL
            );";

        await connection.ExecuteAsync(createDownloadsTableSql);

        var downloadColumns = await connection.QueryAsync<DownloadColumnInfo>("PRAGMA table_info(Downloads);");
        if (!downloadColumns.Any(column => string.Equals(column.Name, "ErrorMessage", StringComparison.OrdinalIgnoreCase)))
            await connection.ExecuteAsync("ALTER TABLE Downloads ADD COLUMN ErrorMessage TEXT NULL;");

        const string createSegmentsTableSql = @"
            CREATE TABLE IF NOT EXISTS DownloadSegments (
                Id TEXT PRIMARY KEY,
                DownloadItemId TEXT NOT NULL,
                StartByte INTEGER NOT NULL,
                EndByte INTEGER NOT NULL,
                DownloadedBytes INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                FOREIGN KEY (DownloadItemId) REFERENCES Downloads(Id) ON DELETE CASCADE
            );";

        await connection.ExecuteAsync(createSegmentsTableSql);

        const string createCategoriesTableSql = @"
            CREATE TABLE IF NOT EXISTS Categories (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                CategoryType INTEGER NOT NULL,
                IconName TEXT NOT NULL
            );";

        await connection.ExecuteAsync(createCategoriesTableSql);

        const string createSettingsTableSql = @"
            CREATE TABLE IF NOT EXISTS ApplicationSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                Category TEXT NOT NULL,
                DataType TEXT NOT NULL
            );";

        await connection.ExecuteAsync(createSettingsTableSql);
    }

    private sealed class DownloadColumnInfo
    {
        public string Name { get; set; } = string.Empty;
    }
}
