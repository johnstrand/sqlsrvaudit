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

        Assert.Contains("### Rule `IDX-001` - Duplicate index definitions detected (1)", markdown, StringComparison.Ordinal);
        Assert.Contains("### Rule `STAT-002` - Statistics configuration issues (1)", markdown, StringComparison.Ordinal);
    }
}
