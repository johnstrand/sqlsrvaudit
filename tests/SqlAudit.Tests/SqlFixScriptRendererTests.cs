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
            CreateFinding("IDX-001", "Duplicate index", "SELECT 1;", requiresWindow: false),
            CreateFinding("IDX-002", "No script", fixScript: null, requiresWindow: false),
        ]);

        var rendered = SqlFixScriptRenderer.Render(report);

        Assert.Single(rendered.NoWindowScripts);
        Assert.Empty(rendered.RequiresWindowScripts);
        Assert.Contains("-- Finding: IDX-001 - Duplicate index", rendered.CombinedScript, StringComparison.Ordinal);
        Assert.DoesNotContain("IDX-002", rendered.CombinedScript, StringComparison.Ordinal);
        Assert.Contains("GO", rendered.CombinedScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_RoutesScriptsByServiceWindow()
    {
        var report = CreateReport(
        [
            CreateFinding("IDX-001", "Safe fix", "SELECT 1;", requiresWindow: false),
            CreateFinding("IDX-002", "Risky fix", "SELECT 2;", requiresWindow: true),
        ]);

        var rendered = SqlFixScriptRenderer.Render(report);

        Assert.Single(rendered.NoWindowScripts);
        Assert.Single(rendered.RequiresWindowScripts);
        Assert.Contains("idx-001", rendered.NoWindowScripts.Keys.First(), StringComparison.Ordinal);
        Assert.Contains("idx-002", rendered.RequiresWindowScripts.Keys.First(), StringComparison.Ordinal);
    }

    [Fact]
    public void Render_CreatesStableSafeFileNames()
    {
        var veryLongTitle = new string('A', 180);
        var report = CreateReport(
        [
            CreateFinding("IDX-001", veryLongTitle, "SELECT 1;", requiresWindow: false),
        ]);

        var rendered = SqlFixScriptRenderer.Render(report);
        var fileName = Assert.Single(rendered.NoWindowScripts).Key;

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

    private static AuditFinding CreateFinding(string id, string title, string? fixScript, bool requiresWindow = true)
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
            ServiceWindow = requiresWindow ? ServiceWindowAdvisor.Yes("window") : ServiceWindowAdvisor.No("safe"),
            FixScript = fixScript,
        };
    }
}
