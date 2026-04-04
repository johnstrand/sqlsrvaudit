using SqlAudit.Core.Models;
using SqlAudit.Reporting;

namespace SqlAudit.Tests;

public sealed class SqlFixScriptRendererTests
{
    [Fact]
    public void Render_IncludesOnlyFindingsWithFixScripts()
    {
        var report = CreateReport(
        [
            CreateFinding("IDX-001", "Duplicate index", "SELECT 1;"),
            CreateFinding("IDX-002", "No script", fixScript: null),
        ]);

        var rendered = SqlFixScriptRenderer.Render(report);

        Assert.Single(rendered.IndividualScripts);
        Assert.Contains("-- Finding: IDX-001 - Duplicate index", rendered.CombinedScript, StringComparison.Ordinal);
        Assert.DoesNotContain("IDX-002", rendered.CombinedScript, StringComparison.Ordinal);
        Assert.Contains("GO", rendered.CombinedScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_CreatesStableSafeFileNames()
    {
        var veryLongTitle = new string('A', 180);
        var report = CreateReport(
        [
            CreateFinding("IDX-001", veryLongTitle, "SELECT 1;"),
        ]);

        var rendered = SqlFixScriptRenderer.Render(report);
        var fileName = Assert.Single(rendered.IndividualScripts).Key;

        Assert.EndsWith(".sql", fileName, StringComparison.Ordinal);
        Assert.True(fileName.Length <= 104);
        Assert.DoesNotContain(" ", fileName, StringComparison.Ordinal);
        Assert.Equal(fileName.ToLowerInvariant(), fileName);
    }

    private static AuditReport CreateReport(IReadOnlyList<AuditFinding> findings)
    {
        return new AuditReport
        {
            ServerName = "server01",
            DatabaseName = "DbA",
            Edition = "Developer",
            ProductVersion = "16.0",
            CapturedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Findings = findings,
        };
    }

    private static AuditFinding CreateFinding(string id, string title, string? fixScript)
    {
        return new AuditFinding
        {
            Id = id,
            Title = title,
            Category = "Indexes",
            Severity = AuditSeverity.Medium,
            DatabaseObject = "[dbo].[Books]",
            Description = "desc",
            Impact = "impact",
            Recommendation = "reco",
            ServiceWindow = ServiceWindowAdvisor.Yes("window"),
            FixScript = fixScript,
        };
    }
}
