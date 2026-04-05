using System.Text.Json;
using SqlAudit.Core.Models;
using SqlAudit.Reporting;

namespace SqlAudit.Tests;

public sealed class JsonReportRendererTests
{
    [Fact]
    public void Render_ProducesIndentedJsonWithServiceWindowFlag()
    {
        var report = new AuditReport
        {
            ServerName = "server01",
            DatabaseName = "DbA",
            Edition = "Developer Edition",
            ProductVersion = "16.0",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Findings =
            [
                new AuditFinding
                {
                    Id = "IDX-1",
                    Title = "Test finding",
                    Category = "Indexes",
                    Severity = AuditSeverity.High,
                    DatabaseObject = "[dbo].[Orders]",
                    Description = "description",
                    Impact = "impact",
                    Recommendation = "recommendation",
                    ServiceWindow = ServiceWindowAdvisor.Yes("needs window"),
                    FixScript = "SELECT 1;",
                    Evidence = [new FindingEvidence("Rows", "1000")],
                },
            ],
            TopResourceIntensiveQueries =
            [
                new ResourceIntensiveQueryInfo(
                    QueryHash: "0x1234",
                    ExecutionCount: 12,
                    TotalCpuMs: 500.5m,
                    AverageCpuMs: 41.7m,
                    TotalDurationMs: 1000.5m,
                    AverageDurationMs: 83.4m,
                    TotalLogicalReads: 40000,
                    TotalLogicalWrites: 200,
                    LastExecutionUtc: null,
                    QueryText: "SELECT 1"),
            ],
        };

        var json = JsonReportRenderer.Render(report);
        using var document = JsonDocument.Parse(json);

        var findings = document.RootElement.GetProperty("Findings");
        var first = findings.EnumerateArray().First();

        Assert.Equal("High", first.GetProperty("Severity").GetString());
        Assert.True(first.GetProperty("ServiceWindow").GetProperty("RequiresServiceWindow").GetBoolean());

        var topQueries = document.RootElement.GetProperty("TopResourceIntensiveQueries");
        var query = topQueries.EnumerateArray().First();
        Assert.Equal("0x1234", query.GetProperty("QueryHash").GetString());
        Assert.Equal(40000L, query.GetProperty("TotalLogicalReads").GetInt64());
    }
}
