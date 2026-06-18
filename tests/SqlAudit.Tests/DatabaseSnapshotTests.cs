using SqlAudit.Core.Models;

namespace SqlAudit.Tests.Models;

public sealed class DatabaseSnapshotTests
{
    private static DatabaseSnapshot CreateSnapshot(string edition, bool isAzureSql = false) =>
        new()
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            ServerName = "TestServer",
            DatabaseName = "TestDb",
            Edition = edition,
            ProductVersion = "15.0",
            CompatibilityLevel = 150,
            IsAzureSql = isAzureSql,
            AutoCreateStatisticsOn = true,
            AutoUpdateStatisticsOn = true,
            Tables = [],
            Indexes = [],
            IndexUsage = [],
            IndexPhysicalStats = [],
            ForeignKeys = [],
            Statistics = [],
            IdentityColumns = []
        };

    [Theory]
    [InlineData("Enterprise Edition", false, true)]
    [InlineData("Enterprise Edition: Core-based Licensing", false, true)]
    [InlineData("Developer Edition", false, true)]
    [InlineData("Standard Edition", false, false)]
    [InlineData("Express Edition", false, false)]
    [InlineData("Web Edition", false, false)]
    [InlineData("SQL Azure", true, true)]
    [InlineData("Standard Edition", true, true)] // Even if standard, if IsAzureSql is true, it should return true
    public void SupportsOnlineIndexOperations_ReturnsExpectedResult(string edition, bool isAzureSql, bool expected)
    {
        var snapshot = CreateSnapshot(edition, isAzureSql);
        Assert.Equal(expected, snapshot.SupportsOnlineIndexOperations);
    }
}
