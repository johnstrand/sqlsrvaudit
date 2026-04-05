using SqlAudit.Core.Models;
using SqlAudit.Reporting;

namespace SqlAudit.Tests;

public sealed class MarkdownReportRendererTests
{
    [Fact]
    public void Render_IncludesConfiguredExclusions()
    {
        var report = new AuditReport
        {
            ServerName = "server01",
            DatabaseName = "DbA",
            Edition = "Developer Edition",
            ProductVersion = "16.0",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            ExcludedSchemas = ["archive"],
            ExcludedTables = ["Book_Backup", "dbo.Legacy_Book_Backup"],
            Findings = [],
        };

        var markdown = MarkdownReportRenderer.Render(report);

        Assert.Contains("<a id=\"top\"></a>", markdown, StringComparison.Ordinal);
        Assert.Contains("### Exclusions", markdown, StringComparison.Ordinal);
        Assert.Contains("- Schemas: `archive`", markdown, StringComparison.Ordinal);
        Assert.Contains("- Tables: `Book_Backup`, `dbo.Legacy_Book_Backup`", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GroupsFindingsByRuleAndLinksFromCheckExecution()
    {
        var report = new AuditReport
        {
            ServerName = "server01",
            DatabaseName = "DbA",
            Edition = "Developer Edition",
            ProductVersion = "16.0",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            CheckExecutions =
            [
                new CheckExecutionResult("IDX-001", "Duplicate index definitions detected", "Indexes", CheckExecutionStatus.Success, 200, 1, null),
                new CheckExecutionResult("IDX-005", "Fragmented indexes", "Indexes", CheckExecutionStatus.Success, 150, 0, null),
                new CheckExecutionResult("STAT-002", "Statistics configuration issues", "Statistics", CheckExecutionStatus.Success, 100, 1, null),
            ],
            Findings =
            [
                new AuditFinding
                {
                    Id = "IDX-001-1-2",
                    Title = "Duplicate index definitions detected",
                    Category = "Indexes",
                    Severity = AuditSeverity.Medium,
                    DatabaseObject = "[dbo].[Books]",
                    Description = "desc",
                    Impact = "impact",
                    Recommendation = "reco",
                    ServiceWindow = ServiceWindowAdvisor.Yes("window"),
                },
                new AuditFinding
                {
                    Id = "STAT-002-AUTO-UPDATE",
                    Title = "AUTO_UPDATE_STATISTICS is OFF",
                    Category = "Statistics",
                    Severity = AuditSeverity.High,
                    DatabaseObject = "DbA",
                    Description = "desc",
                    Impact = "impact",
                    Recommendation = "reco",
                    ServiceWindow = ServiceWindowAdvisor.No("metadata"),
                },
            ],
        };

        var markdown = MarkdownReportRenderer.Render(report);

        Assert.Contains("[IDX-001](#rule-idx-001)", markdown, StringComparison.Ordinal);
        Assert.Contains("[STAT-002](#rule-stat-002)", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("[IDX-005](#rule-idx-005)", markdown, StringComparison.Ordinal);

        const string idxSection = "### Rule `IDX-001` - Duplicate index definitions detected (1)";
        const string statSection = "### Rule `STAT-002` - Statistics configuration issues (1)";
        var idxPosition = markdown.IndexOf(idxSection, StringComparison.Ordinal);
        var statPosition = markdown.IndexOf(statSection, StringComparison.Ordinal);

        Assert.True(idxPosition >= 0);
        Assert.True(statPosition >= 0);
        Assert.True(idxPosition < statPosition);
        Assert.Contains("#### [Medium] Duplicate index definitions detected [^](#top)", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_IncludesTopResourceIntensiveQueries()
    {
        var report = new AuditReport
        {
            ServerName = "server01",
            DatabaseName = "DbA",
            Edition = "Developer Edition",
            ProductVersion = "16.0",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            TopResourceIntensiveQueries =
            [
                new ResourceIntensiveQueryInfo(
                    QueryHash: "0xABCD",
                    ExecutionCount: 42,
                    TotalCpuMs: 12500.45m,
                    AverageCpuMs: 297.63m,
                    TotalDurationMs: 23100.10m,
                    AverageDurationMs: 550.00m,
                    TotalLogicalReads: 1000000,
                    TotalLogicalWrites: 120,
                    LastExecutionUtc: new DateTimeOffset(2026, 4, 5, 12, 0, 0, TimeSpan.Zero),
                    QueryText: "SELECT * FROM dbo.Orders WHERE OrderDate >= @P1"),
            ],
            Findings = [],
        };

        var markdown = MarkdownReportRenderer.Render(report);

        Assert.Contains("### Top Resource-Intensive Queries", markdown, StringComparison.Ordinal);
        Assert.Contains("| `0xABCD` | 42 | 12500.45 | 297.63 | 1000000 | 2026-04-05 12:00:00Z |", markdown, StringComparison.Ordinal);
        Assert.Contains("- `0xABCD` SELECT * FROM dbo.Orders WHERE OrderDate >= @P1", markdown, StringComparison.Ordinal);
    }
}
