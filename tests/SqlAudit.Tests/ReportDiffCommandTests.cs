using SqlAudit.Cli;
using SqlAudit.Core.Models;

namespace SqlAudit.Tests;

public sealed class ReportDiffCommandTests
{
    [Fact]
    public void Analyze_DetectsNewFixedAndRegressedFindings()
    {
        var previous = CreateReport(
            CreateFinding("IDX-001-1", "[dbo].[Orders]", AuditSeverity.Medium),
            CreateFinding("FK-002-1", "[dbo].[Items]", AuditSeverity.High));

        var current = CreateReport(
            CreateFinding("IDX-001-1", "[dbo].[Orders]", AuditSeverity.Critical),
            CreateFinding("STAT-002-AUTO-CREATE", "MyDb", AuditSeverity.High));

        var diff = ReportDiffCommand.Analyze(previous, current);

        Assert.Single(diff.NewFindings);
        Assert.Single(diff.FixedFindings);
        Assert.Single(diff.Regressed);
        Assert.Empty(diff.Improved);
    }

    private static AuditReport CreateReport(params AuditFinding[] findings)
    {
        return new AuditReport
        {
            ServerName = "server",
            DatabaseName = "db",
            Edition = "Developer",
            ProductVersion = "16.0",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Findings = findings,
            CheckExecutions = [],
            SuppressionSummary = SuppressionSummary.None,
        };
    }

    private static AuditFinding CreateFinding(string id, string dbObject, AuditSeverity severity)
    {
        return new AuditFinding
        {
            Id = id,
            Title = "title",
            Category = "cat",
            Severity = severity,
            DatabaseObject = dbObject,
            Description = "desc",
            Impact = "impact",
            Recommendation = "rec",
            ServiceWindow = ServiceWindowAdvisor.No("none"),
        };
    }
}
