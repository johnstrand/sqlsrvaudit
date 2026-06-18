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
                new CheckExecutionResult("IDX-001", "Duplicate index definitions detected", "Indexes", CheckExecutionStatus.Success, 200, 1, ErrorMessage: null),
                new CheckExecutionResult("IDX-005", "Fragmented indexes", "Indexes", CheckExecutionStatus.Success, 150, 0, ErrorMessage: null),
                new CheckExecutionResult("STAT-002", "Statistics configuration issues", "Statistics", CheckExecutionStatus.Success, 100, 1, ErrorMessage: null),
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

    [Fact]
    public void Render_ShowsNoTelemetryMessage_WhenTopResourceIntensiveQueriesEmpty()
    {
        var report = new AuditReport
        {
            ServerName = "server01",
            DatabaseName = "DbA",
            Edition = "Developer Edition",
            ProductVersion = "16.0",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            TopResourceIntensiveQueries = [],
            Findings = [],
        };

        var markdown = MarkdownReportRenderer.Render(report);

        Assert.Contains("### Top Resource-Intensive Queries", markdown, StringComparison.Ordinal);
        Assert.Contains("No query runtime telemetry available.", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| Query Hash | Executions |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_IncludesExtendedOperationalTelemetrySections()
    {
        var report = new AuditReport
        {
            ServerName = "server01",
            DatabaseName = "DbA",
            Edition = "Developer Edition",
            ProductVersion = "16.0",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            TopWaitStats =
            [
                new WaitStatInfo("LCK_M_X", 4, 120.0m, 110.0m, 10.0m, 30000.0m, "Locking"),
            ],
            QueryStoreRegressions =
            [
                new QueryStoreRegressionInfo(11, 20.0m, 50.0m, 2.5m, 100, LastExecutionUtc: null, "SELECT 1"),
            ],
            DeadlockSummary = new DeadlockSummaryInfo(2, LastDeadlockUtc: null),
            ActiveBlockingSessions =
            [
                new BlockingSessionInfo(55, 56, "LCK_M_S", 9000, "KEY", "SELECT * FROM dbo.T"),
            ],
            MissingIndexSignals =
            [
                new MissingIndexSignalInfo(1, "dbo", "Orders", "[CustomerId]", string.Empty, "[OrderDate]", 400, 10, 120.0m, 95.0m, 49020.0m, 3, "Signal passes read-benefit guardrails."),
            ],
            LogHealth = new LogHealthInfo(2048m, 1536m, 75m, 150, 12, "ACTIVE_TRANSACTION"),
            TempDbPressure = new TempDbPressureInfo(256m, 128m, 64m, 512m),
            FileGrowthHealth =
            [
                new FileGrowthHealthInfo(1, "DbA", "ROWS", "C:\\Data\\DbA.mdf", 1024m, IsPercentGrowth: false, 64m, MaxSizeMb: null, "64 MB", "Growth setting looks reasonable."),
            ],
            BackupPosture = new BackupPostureInfo("FULL", LastFullBackupUtc: null, LastDifferentialBackupUtc: null, LastLogBackupUtc: null, 12m, 4m, 1m),
            SecurityHygieneIssues =
            [
                new SecurityHygieneIssueInfo("DbOwnerMembership", AuditSeverity.Medium, "legacy_user", "Principal is member of db_owner role."),
            ],
            TableGrowthForecasts =
            [
                new TableGrowthForecastInfo("[dbo].[Orders]", 500m, 650m, 150m, 10m, 1100m, 2000m),
            ],
            Findings = [],
        };

        var markdown = MarkdownReportRenderer.Render(report);

        Assert.Contains("### Wait Stats Breakdown", markdown, StringComparison.Ordinal);
        Assert.Contains("### Query Store Regressions", markdown, StringComparison.Ordinal);
        Assert.Contains("### Blocking and Deadlocks", markdown, StringComparison.Ordinal);
        Assert.Contains("### Missing Index Signals", markdown, StringComparison.Ordinal);
        Assert.Contains("### Log Health", markdown, StringComparison.Ordinal);
        Assert.Contains("### Tempdb Pressure", markdown, StringComparison.Ordinal);
        Assert.Contains("### File Growth Health", markdown, StringComparison.Ordinal);
        Assert.Contains("### Backup and Restore Posture", markdown, StringComparison.Ordinal);
        Assert.Contains("### Security Hygiene", markdown, StringComparison.Ordinal);
        Assert.Contains("### Growth Forecasting", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_IncludesCollectionWarningsWhenPresent()
    {
        var report = new AuditReport
        {
            ServerName = "server01",
            DatabaseName = "DbA",
            Edition = "Developer Edition",
            ProductVersion = "16.0",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            CollectionWarnings =
            [
                new CollectionWarning(
                    "Dynamic Management Views",
                    "The account lacks VIEW SERVER STATE or VIEW DATABASE STATE permission."),
                new CollectionWarning(
                    "Index Usage Statistics",
                    "The user does not have permission to perform this action."),
            ],
            Findings = [],
        };

        var markdown = MarkdownReportRenderer.Render(report);

        Assert.Contains("### ⚠ Data Collection Warnings", markdown, StringComparison.Ordinal);
        Assert.Contains("| Dynamic Management Views |", markdown, StringComparison.Ordinal);
        Assert.Contains("| Index Usage Statistics |", markdown, StringComparison.Ordinal);
        Assert.Contains("The account lacks VIEW SERVER STATE", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_OmitsCollectionWarningsSectionWhenNonePresent()
    {
        var report = new AuditReport
        {
            ServerName = "server01",
            DatabaseName = "DbA",
            Edition = "Developer Edition",
            ProductVersion = "16.0",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Findings = [],
        };

        var markdown = MarkdownReportRenderer.Render(report);

        Assert.DoesNotContain("### ⚠ Data Collection Warnings", markdown, StringComparison.Ordinal);
    }
}
