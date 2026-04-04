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
        };

        var json = JsonReportRenderer.Render(report);
        using var document = JsonDocument.Parse(json);

        var findings = document.RootElement.GetProperty("Findings");
        var first = findings.EnumerateArray().First();

        Assert.Equal("High", first.GetProperty("Severity").GetString());
        Assert.True(first.GetProperty("ServiceWindow").GetProperty("RequiresServiceWindow").GetBoolean());
    }
}
