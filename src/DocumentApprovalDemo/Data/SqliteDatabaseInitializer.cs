using Microsoft.Data.Sqlite;

namespace DocumentApprovalDemo.Data;

public static class SqliteDatabaseInitializer
{
    public static string? EnsureParentDirectory(string connectionString, string contentRootPath)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
            return null;

        var databasePath = Path.IsPathRooted(dataSource)
            ? Path.GetFullPath(dataSource)
            : Path.GetFullPath(Path.Combine(contentRootPath, dataSource));
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        return databasePath;
    }
}

