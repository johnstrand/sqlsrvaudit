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
