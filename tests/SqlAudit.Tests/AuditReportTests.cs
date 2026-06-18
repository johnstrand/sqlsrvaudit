using System;
using System.Collections.Generic;
using System.Text.Json;
using SqlAudit.Core.Models;
using Xunit;

namespace SqlAudit.Tests;

public sealed class AuditReportTests
{
    [Fact]
    public void SeverityCounts_AggregatesProperly()
    {
        var report = new AuditReport
        {
            ServerName = "TestServer",
            DatabaseName = "TestDb",
            Edition = "Developer",
            ProductVersion = "16.0",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Findings =
            [
                new AuditFinding { Id = "ID1", Title = "High Severity", Description = "Desc", Impact = "Impact", Recommendation = "Rec", Category = "Category A", Severity = AuditSeverity.High, DatabaseObject = "Obj", ServiceWindow = ServiceWindowAdvisor.Yes("Test") },
                new AuditFinding { Id = "ID2", Title = "High Severity 2", Description = "Desc", Impact = "Impact", Recommendation = "Rec", Category = "Category B", Severity = AuditSeverity.High, DatabaseObject = "Obj", ServiceWindow = ServiceWindowAdvisor.Yes("Test") },
                new AuditFinding { Id = "ID3", Title = "Medium Severity", Description = "Desc", Impact = "Impact", Recommendation = "Rec", Category = "Category A", Severity = AuditSeverity.Medium, DatabaseObject = "Obj", ServiceWindow = ServiceWindowAdvisor.Yes("Test") }
            ]
        };

        var counts = report.SeverityCounts;

        Assert.Equal(2, counts.Count);
        Assert.Equal(2, counts[AuditSeverity.High]);
        Assert.Equal(1, counts[AuditSeverity.Medium]);
        Assert.False(counts.ContainsKey(AuditSeverity.Critical));
    }

    [Fact]
    public void CategoryCounts_AggregatesProperly()
    {
        var report = new AuditReport
        {
            ServerName = "TestServer",
            DatabaseName = "TestDb",
            Edition = "Developer",
            ProductVersion = "16.0",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Findings =
            [
                new AuditFinding { Id = "ID1", Title = "Finding 1", Description = "Desc", Impact = "Impact", Recommendation = "Rec", Category = "Performance", Severity = AuditSeverity.High, DatabaseObject = "Obj", ServiceWindow = ServiceWindowAdvisor.Yes("Test") },
                new AuditFinding { Id = "ID2", Title = "Finding 2", Description = "Desc", Impact = "Impact", Recommendation = "Rec", Category = "performance", Severity = AuditSeverity.Medium, DatabaseObject = "Obj", ServiceWindow = ServiceWindowAdvisor.Yes("Test") },
                new AuditFinding { Id = "ID3", Title = "Finding 3", Description = "Desc", Impact = "Impact", Recommendation = "Rec", Category = "Security", Severity = AuditSeverity.Critical, DatabaseObject = "Obj", ServiceWindow = ServiceWindowAdvisor.Yes("Test") }
            ]
        };

        var counts = report.CategoryCounts;

        Assert.Equal(2, counts.Count);
        Assert.Equal(2, counts["Performance"]); // Case insensitive
        Assert.Equal(1, counts["Security"]);
    }

    [Fact]
    public void Serialization_RoundTrip_PreservesData()
    {
        var original = new AuditReport
        {
            ServerName = "TestServer",
            DatabaseName = "TestDb",
            Edition = "Developer",
            ProductVersion = "16.0",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Findings =
            [
                new AuditFinding { Id = "ID1", Title = "Finding 1", Description = "Desc", Impact = "Impact", Recommendation = "Rec", Category = "Category A", Severity = AuditSeverity.High, DatabaseObject = "Obj", ServiceWindow = ServiceWindowAdvisor.Yes("Test") }
            ],
            TopResourceIntensiveQueries =
            [
                new ResourceIntensiveQueryInfo("0xHash", 100, 1000m, 10m, 500m, 5m, 200, 50, DateTimeOffset.UtcNow, "SELECT * FROM T1")
            ],
            SuppressionSummary = new SuppressionSummary(1, 0, 1, 1, 0)
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<AuditReport>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ServerName, deserialized.ServerName);
        Assert.Equal(original.DatabaseName, deserialized.DatabaseName);
        Assert.Equal(original.Edition, deserialized.Edition);
        Assert.Equal(original.ProductVersion, deserialized.ProductVersion);
        Assert.Equal(original.CapturedAtUtc, deserialized.CapturedAtUtc);

        Assert.Single(deserialized.Findings);
        Assert.Equal(original.Findings[0].Id, deserialized.Findings[0].Id);

        Assert.Single(deserialized.TopResourceIntensiveQueries);
        Assert.Equal(original.TopResourceIntensiveQueries[0].QueryHash, deserialized.TopResourceIntensiveQueries[0].QueryHash);

        Assert.Equal(original.SuppressionSummary.TotalRules, deserialized.SuppressionSummary.TotalRules);
        Assert.Equal(original.SuppressionSummary.SuppressedFindings, deserialized.SuppressionSummary.SuppressedFindings);
    }
}
