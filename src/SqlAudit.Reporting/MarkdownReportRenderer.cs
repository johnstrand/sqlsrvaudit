using SqlAudit.Core.Models;
using System.Text;

namespace SqlAudit.Reporting;

public static class MarkdownReportRenderer
{
    public static string Render(AuditReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# SQL Audit Health Report");
        sb.AppendLine();
        sb.AppendLine($"- Schema Version: `{EscapeInline(report.SchemaVersion)}`");
        sb.AppendLine($"- Generated (UTC): {report.CapturedAtUtc:u}");
        sb.AppendLine($"- Server: `{EscapeInline(report.ServerName)}`");
        sb.AppendLine($"- Database: `{EscapeInline(report.DatabaseName)}`");
        sb.AppendLine($"- Edition: `{EscapeInline(report.Edition)}`");
        sb.AppendLine($"- Product Version: `{EscapeInline(report.ProductVersion)}`");
        sb.AppendLine();
        sb.AppendLine("## Scorecard");
        sb.AppendLine();
        sb.AppendLine("| Severity | Count |");
        sb.AppendLine("|---|---:|");

        foreach (var severity in Enum.GetValues<AuditSeverity>().OrderBy(s => s))
        {
            var count = report.SeverityCounts.TryGetValue(severity, out var value) ? value : 0;
            sb.AppendLine($"| {severity} | {count} |");
        }

        sb.AppendLine();
        sb.AppendLine("### Categories");
        sb.AppendLine();
        sb.AppendLine("| Category | Count |");
        sb.AppendLine("|---|---:|");
        foreach (var category in report.CategoryCounts.OrderByDescending(kvp => kvp.Value).ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"| {EscapeInline(category.Key)} | {category.Value} |");
        }

        sb.AppendLine();
        sb.AppendLine("### Suppressions");
        sb.AppendLine();
        sb.AppendLine($"- Total rules: {report.SuppressionSummary.TotalRules}");
        sb.AppendLine($"- Active rules: {report.SuppressionSummary.ActiveRules}");
        sb.AppendLine($"- Expired rules: {report.SuppressionSummary.ExpiredRules}");
        sb.AppendLine($"- Suppressed findings: {report.SuppressionSummary.SuppressedFindings}");
        sb.AppendLine($"- Remaining findings: {report.SuppressionSummary.RemainingFindings}");
        sb.AppendLine();

        if (report.CheckExecutions.Count > 0)
        {
            sb.AppendLine("### Check Execution");
            sb.AppendLine();
            sb.AppendLine("| Check Id | Status | Duration (ms) | Findings | Title |");
            sb.AppendLine("|---|---|---:|---:|---|");

            foreach (var check in report.CheckExecutions.OrderByDescending(c => c.DurationMs).ThenBy(c => c.CheckId, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"| {EscapeInline(check.CheckId)} | {check.Status} | {check.DurationMs} | {check.FindingCount} | {EscapeInline(check.Title)} |");
            }

            sb.AppendLine();
        }

        var quickWins = report.Findings
            .Where(f => !f.ServiceWindow.RequiresServiceWindow)
            .Take(10)
            .ToArray();

        sb.AppendLine("### Quick Wins");
        sb.AppendLine();
        if (quickWins.Length == 0)
        {
            sb.AppendLine("No immediate low-risk fixes were identified.");
        }
        else
        {
            foreach (var finding in quickWins)
            {
                sb.AppendLine($"- `{EscapeInline(finding.Id)}` {EscapeInline(finding.Title)} on `{EscapeInline(finding.DatabaseObject)}`");
            }
        }

        sb.AppendLine();
        sb.AppendLine("### Top Risky Objects");
        sb.AppendLine();
        var topRiskyObjects = report.Findings
            .GroupBy(f => f.DatabaseObject)
            .Select(g => new
            {
                DatabaseObject = g.Key,
                Score = g.Sum(f => Score(f.Severity)),
                Count = g.Count()
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Count)
            .ThenBy(x => x.DatabaseObject, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

        if (topRiskyObjects.Length == 0)
        {
            sb.AppendLine("No risky objects identified.");
        }
        else
        {
            sb.AppendLine("| Object | Risk Score | Findings |");
            sb.AppendLine("|---|---:|---:|");
            foreach (var entry in topRiskyObjects)
            {
                sb.AppendLine($"| `{EscapeInline(entry.DatabaseObject)}` | {entry.Score} | {entry.Count} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Total findings: **{report.Findings.Count}**");
        sb.AppendLine();

        if (report.Findings.Count == 0)
        {
            sb.AppendLine("## Findings");
            sb.AppendLine();
            sb.AppendLine("No issues were detected by the enabled check set.");
            return sb.ToString();
        }

        sb.AppendLine("## Findings");
        sb.AppendLine();

        foreach (var finding in report.Findings)
        {
            sb.AppendLine($"### [{finding.Severity}] {EscapeInline(finding.Title)}");
            sb.AppendLine();
            sb.AppendLine($"- Id: `{EscapeInline(finding.Id)}`");
            sb.AppendLine($"- Category: `{EscapeInline(finding.Category)}`");
            sb.AppendLine($"- Object: `{EscapeInline(finding.DatabaseObject)}`");
            sb.AppendLine($"- Service Window Required: **{finding.ServiceWindow.RequiresServiceWindow}**");
            sb.AppendLine($"- Service Window Reason: {EscapeInline(finding.ServiceWindow.Reason)}");
            sb.AppendLine($"- Description: {EscapeInline(finding.Description)}");
            sb.AppendLine($"- Impact: {EscapeInline(finding.Impact)}");
            sb.AppendLine($"- Recommendation: {EscapeInline(finding.Recommendation)}");

            if (finding.Evidence.Count > 0)
            {
                sb.AppendLine("- Evidence:");
                foreach (var evidence in finding.Evidence)
                {
                    sb.AppendLine($"  - {EscapeInline(evidence.Name)}: `{EscapeInline(evidence.Value)}`");
                }
            }

            if (!string.IsNullOrWhiteSpace(finding.FixScript))
            {
                sb.AppendLine("- Fix Script:");
                sb.AppendLine("```sql");
                sb.AppendLine(finding.FixScript.Trim());
                sb.AppendLine("```");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string EscapeInline(string input) => input.Replace("|", "\\|", StringComparison.Ordinal);

    private static int Score(AuditSeverity severity) => severity switch
    {
        AuditSeverity.Critical => 5,
        AuditSeverity.High => 4,
        AuditSeverity.Medium => 3,
        AuditSeverity.Low => 2,
        _ => 1
    };
}
