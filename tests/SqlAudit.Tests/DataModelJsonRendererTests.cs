using System.Text.Json;
using SqlAudit.Core.Models;
using SqlAudit.Reporting;

namespace SqlAudit.Tests;

public sealed class DataModelJsonRendererTests
{
    [Fact]
    public void Render_ProducesIndentedSnapshotJson()
    {
        var snapshot = new DatabaseSnapshot
        {
            ServerName = "server01",
            DatabaseName = "DbA",
            Edition = "Developer Edition",
            ProductVersion = "16.0",
            CompatibilityLevel = 160,
            IsAzureSql = false,
            AutoCreateStatisticsOn = true,
            AutoUpdateStatisticsOn = true,
            Tables = [new TableInfo(1, "dbo", "Orders", 1000, 12.5m, HasPrimaryKey: true, IsHeap: false)],
            Indexes = [],
            IndexUsage = [],
            IndexPhysicalStats = [],
            ForeignKeys = [],
            Statistics = [],
            IdentityColumns = [],
            TopResourceIntensiveQueries =
            [
                new ResourceIntensiveQueryInfo(
                    QueryHash: "0xABC",
                    ExecutionCount: 3,
                    TotalCpuMs: 120.5m,
                    AverageCpuMs: 40.2m,
                    TotalDurationMs: 300.5m,
                    AverageDurationMs: 100.2m,
                    TotalLogicalReads: 999,
                    TotalLogicalWrites: 12,
                    LastExecutionUtc: null,
                    QueryText: "SELECT 1"),
            ],
        };

        var json = DataModelJsonRenderer.Render(snapshot);
        using var doc = JsonDocument.Parse(json);

        Assert.True(json.Contains(Environment.NewLine, StringComparison.Ordinal));
        Assert.Equal("server01", doc.RootElement.GetProperty("ServerName").GetString());
        Assert.Equal("Orders", doc.RootElement.GetProperty("Tables")[0].GetProperty("TableName").GetString());
        Assert.Equal("0xABC", doc.RootElement.GetProperty("TopResourceIntensiveQueries")[0].GetProperty("QueryHash").GetString());
    }
}
