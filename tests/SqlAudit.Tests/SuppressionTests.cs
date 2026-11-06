using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;

namespace SqlAudit.Tests;

public sealed class SuppressionTests
{
    [Fact]
    public void Apply_SuppressesByFindingIdAndWildcardObjectPattern()
    {
        var findings = new[]
        {
            CreateFinding("IDX-001-10", "[dbo].[Orders]"),
            CreateFinding("IDX-001-11", "[dbo].[ArchiveOrders]"),
            CreateFinding("FK-002-1", "[dbo].[OrderItems]"),
        };

        var rules = new[]
        {
            new AuditSuppressionRule("IDX-001", "[dbo].[*Orders]", "known", ExpiresUtc: null),
        };

        var outcome = AuditFindingSuppressor.Apply(findings, rules, DateTimeOffset.UtcNow);

        Assert.Single(outcome.Findings);
        Assert.Equal("FK-002-1", outcome.Findings[0].Id);
        Assert.Equal(2, outcome.Summary.SuppressedFindings);
        Assert.Equal(1, outcome.Summary.ActiveRules);
    }

    [Fact]
    public void Apply_IgnoresExpiredRules()
    {
        var findings = new[]
        {
            CreateFinding("IDX-004-1", "[dbo].[Orders]"),
        };

        var rules = new[]
        {
            new AuditSuppressionRule("IDX-004", DatabaseObjectPattern: null, Reason: null, DateTimeOffset.UtcNow.AddDays(-1)),
        };

        var outcome = AuditFindingSuppressor.Apply(findings, rules, DateTimeOffset.UtcNow);

        Assert.Single(outcome.Findings);
        Assert.Equal(1, outcome.Summary.ExpiredRules);
        Assert.Equal(0, outcome.Summary.SuppressedFindings);
    }

    [Fact]
    public void Apply_EmptyFindings_ReflectsTrueActiveAndExpiredRuleCounts()
    {
        var findings = Array.Empty<AuditFinding>();
        var now = DateTimeOffset.UtcNow;
        var rules = new[]
        {
            new AuditSuppressionRule("IDX-001", null, null, ExpiresUtc: null),
            new AuditSuppressionRule("IDX-002", null, null, ExpiresUtc: now.AddDays(1)),
            new AuditSuppressionRule("IDX-003", null, null, ExpiresUtc: now.AddDays(-1)),
        };

        var outcome = AuditFindingSuppressor.Apply(findings, rules, now);

        Assert.Empty(outcome.Findings);
        Assert.Equal(3, outcome.Summary.TotalRules);
        Assert.Equal(2, outcome.Summary.ActiveRules);
        Assert.Equal(1, outcome.Summary.ExpiredRules);
        Assert.Equal(0, outcome.Summary.SuppressedFindings);
        Assert.Equal(0, outcome.Summary.RemainingFindings);
    }

    [Fact]
    public void Apply_TightFindingIdPrefixMatching_DoesNotSuppressUnintendedPrefixes()
    {
        var findings = new[]
        {
            CreateFinding("IDX-001-10", "[dbo].[Orders]"),
            CreateFinding("IDX-0010-1", "[dbo].[Orders]"),
        };

        var rules = new[]
        {
            new AuditSuppressionRule("IDX-001", null, null, ExpiresUtc: null),
        };

        var outcome = AuditFindingSuppressor.Apply(findings, rules, DateTimeOffset.UtcNow);

        Assert.Single(outcome.Findings);
        Assert.Equal("IDX-0010-1", outcome.Findings[0].Id);
        Assert.Equal(1, outcome.Summary.SuppressedFindings);
    }

    private static AuditFinding CreateFinding(string id, string dbObject)
    {
        return new AuditFinding
        {
            Id = id,
            Title = "title",
            Category = "Indexes",
            Severity = AuditSeverity.Medium,
            DatabaseObject = dbObject,
            Description = "desc",
            Impact = "impact",
            Recommendation = "rec",
            ServiceWindow = ServiceWindowAdvisor.No("none"),
        };
    }
}
