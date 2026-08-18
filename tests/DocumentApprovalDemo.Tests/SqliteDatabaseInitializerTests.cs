using DocumentApprovalDemo.Data;
using Xunit;

namespace DocumentApprovalDemo.Tests;

public sealed class SqliteDatabaseInitializerTests
{
    [Fact]
    public void EnsureParentDirectory_CreatesMissingAppDataDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"document-approval-{Guid.NewGuid():N}");
        var expectedDirectory = Path.Combine(root, "App_Data");

        try
        {
            var databasePath = SqliteDatabaseInitializer.EnsureParentDirectory(
                "Data Source=App_Data/document-approval-demo.db", root);

            Assert.Equal(Path.Combine(expectedDirectory, "document-approval-demo.db"), databasePath);
            Assert.True(Directory.Exists(expectedDirectory));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
