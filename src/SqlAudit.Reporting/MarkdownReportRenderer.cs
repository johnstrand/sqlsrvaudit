using SqlAudit.Core.Models;
using System.Text;

namespace SqlAudit.Reporting;

public static class MarkdownReportRenderer
{
    public static string Render(AuditReport report)
    {
        var sb = new StringBuilder();

        WriteHeader(sb, report);
        WriteExclusions(sb, report);
        WriteScorecard(sb, report);
        WriteCategories(sb, report);
        WriteSuppressions(sb, report);
        WriteCheckExecution(sb, report);
        WriteQuickWins(sb, report);
        WriteTopRiskyObjects(sb, report);
        WriteFindings(sb, report);

        return sb.ToString();
    }

    private static void WriteHeader(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("<a id=\"top\"></a>");
        sb.AppendLine("# SQL Audit Health Report");
        sb.AppendLine();
        sb.AppendLine($"- Schema Version: `{EscapeInline(report.SchemaVersion)}`");
        sb.AppendLine($"- Generated (UTC): {report.CapturedAtUtc:u}");
        sb.AppendLine($"- Server: `{EscapeInline(report.ServerName)}`");
        sb.AppendLine($"- Database: `{EscapeInline(report.DatabaseName)}`");
        sb.AppendLine($"- Edition: `{EscapeInline(report.Edition)}`");
        sb.AppendLine($"- Product Version: `{EscapeInline(report.ProductVersion)}`");
        sb.AppendLine();
    }

    private static void WriteScorecard(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("## Scorecard");
        sb.AppendLine();
        sb.AppendLine("| Severity | Count |");
        sb.AppendLine("|---|---:|");

        foreach (var severity in Enum.GetValues<AuditSeverity>().Order())
        {
            var count = report.SeverityCounts.TryGetValue(severity, out var value) ? value : 0;
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"| {severity} | {count} |");
        }

        sb.AppendLine();
    }

    private static void WriteExclusions(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Exclusions");
        sb.AppendLine();
        sb.AppendLine($"- Schemas: {FormatExclusionList(report.ExcludedSchemas)}");
        sb.AppendLine($"- Tables: {FormatExclusionList(report.ExcludedTables)}");
        sb.AppendLine();
    }

    private static void WriteCategories(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Categories");
        sb.AppendLine();
        sb.AppendLine("| Category | Count |");
        sb.AppendLine("|---|---:|");

        foreach (var category in report.CategoryCounts.OrderByDescending(kvp => kvp.Value).ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"| {EscapeInline(category.Key)} | {category.Value} |");
        }

        sb.AppendLine();
    }

    private static void WriteSuppressions(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Suppressions");
        sb.AppendLine();
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- Total rules: {report.SuppressionSummary.TotalRules}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- Active rules: {report.SuppressionSummary.ActiveRules}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- Expired rules: {report.SuppressionSummary.ExpiredRules}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- Suppressed findings: {report.SuppressionSummary.SuppressedFindings}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- Remaining findings: {report.SuppressionSummary.RemainingFindings}");
        sb.AppendLine();
    }

    private static void WriteCheckExecution(StringBuilder sb, AuditReport report)
    {
        if (report.CheckExecutions.Count == 0)
        {
            return;
        }

        var ruleIdsWithFindings = report.Findings
            .Select(finding => GetRuleId(finding.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        sb.AppendLine("### Check Execution");
        sb.AppendLine();
        sb.AppendLine("| Check Id | Status | Duration (ms) | Findings | Title |");
        sb.AppendLine("|---|---|---:|---:|---|");

        foreach (var check in GetOrderedCheckExecutions(report))
        {
            var checkId = ruleIdsWithFindings.Contains(check.CheckId)
                ? $"[{EscapeInline(check.CheckId)}](#{BuildRuleAnchorId(check.CheckId)})"
                : EscapeInline(check.CheckId);
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"| {checkId} | {check.Status} | {check.DurationMs} | {check.FindingCount} | {EscapeInline(check.Title)} |");
        }

        sb.AppendLine();
    }

    private static void WriteQuickWins(StringBuilder sb, AuditReport report)
    {
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
    }

    private static void WriteTopRiskyObjects(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Top Risky Objects");
        sb.AppendLine();

        var topRiskyObjects = report.Findings
            .GroupBy(f => f.DatabaseObject, StringComparer.Ordinal)
            .Select(g => new
            {
                DatabaseObject = g.Key,
                Score = g.Sum(f => Score(f.Severity)),
                Count = g.Count(),
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Count)
            .ThenBy(x => x.DatabaseObject, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

        if (topRiskyObjects.Length == 0)
        {
            sb.AppendLine("No risky objects identified.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Object | Risk Score | Findings |");
        sb.AppendLine("|---|---:|---:|");

        foreach (var entry in topRiskyObjects)
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"| `{EscapeInline(entry.DatabaseObject)}` | {entry.Score} | {entry.Count} |");
        }

        sb.AppendLine();
    }

    private static void WriteFindings(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Total findings: **{report.Findings.Count}**");
        sb.AppendLine();

        if (report.Findings.Count == 0)
        {
            sb.AppendLine("## Findings");
            sb.AppendLine();
            sb.AppendLine("No issues were detected by the enabled check set.");
            return;
        }

        sb.AppendLine("## Findings");
        sb.AppendLine();

        var checkDisplayOrder = GetOrderedCheckExecutions(report)
            .Select((check, index) => new KeyValuePair<string, int>(check.CheckId, index))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var checkTitlesById = report.CheckExecutions
            .ToDictionary(check => check.CheckId, check => check.Title, StringComparer.OrdinalIgnoreCase);
        var groups = report.Findings
            .GroupBy(finding => GetRuleId(finding.Id), StringComparer.OrdinalIgnoreCase)
            .Select(group => new RuleFindingGroup(
                group.Key,
                checkTitlesById.TryGetValue(group.Key, out var title) ? title : null,
                [.. group
                    .OrderBy(finding => finding.Severity)
                    .ThenBy(finding => finding.Category, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(finding => finding.DatabaseObject, StringComparer.OrdinalIgnoreCase)]))
            .OrderBy(group => checkDisplayOrder.TryGetValue(group.RuleId, out var order) ? order : int.MaxValue)
            .ThenBy(group => group.Findings[0].Severity)
            .ThenBy(group => group.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var group in groups)
        {
            var heading = string.IsNullOrWhiteSpace(group.Title)
                ? $"### Rule `{EscapeInline(group.RuleId)}` ({group.Findings.Count})"
                : $"### Rule `{EscapeInline(group.RuleId)}` - {EscapeInline(group.Title)} ({group.Findings.Count})";

            sb.AppendLine($"<a id=\"{BuildRuleAnchorId(group.RuleId)}\"></a>");
            sb.AppendLine(heading);
            sb.AppendLine();

            foreach (var finding in group.Findings)
            {
                WriteFindingSection(sb, finding);
            }
        }
    }

    private static void WriteFindingSection(StringBuilder sb, AuditFinding finding)
    {
        sb.AppendLine($"#### [{finding.Severity}] {EscapeInline(finding.Title)} [^](#top)");
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

    private static string EscapeInline(string input) => input.Replace("|", "\\|", StringComparison.Ordinal);

    private static string FormatExclusionList(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "(none)";
        }

        return string.Join(", ", values.Select(value => $"`{EscapeInline(value)}`"));
    }

    private static string GetRuleId(string findingId)
    {
        const string failurePrefix = "CHECK-FAIL-";
        var candidate = findingId.StartsWith(failurePrefix, StringComparison.OrdinalIgnoreCase)
            ? findingId[failurePrefix.Length..]
            : findingId;

        var parts = candidate.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 1; i < parts.Length; i++)
        {
            if (int.TryParse(parts[i], out _))
            {
                return $"{parts[i - 1]}-{parts[i]}";
            }
        }

        return candidate;
    }

    private static string BuildRuleAnchorId(string ruleId)
    {
        var fragment = new StringBuilder(ruleId.Length);
        var previousDash = false;
        foreach (var ch in ruleId.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                fragment.Append(ch);
                previousDash = false;
                continue;
            }

            if (previousDash)
            {
                continue;
            }

            fragment.Append('-');
            previousDash = true;
        }

        return $"rule-{fragment.ToString().Trim('-')}";
    }

    private sealed record RuleFindingGroup(string RuleId, string? Title, IReadOnlyList<AuditFinding> Findings);

    private static IEnumerable<CheckExecutionResult> GetOrderedCheckExecutions(AuditReport report)
    {
        return report.CheckExecutions
            .OrderByDescending(check => check.DurationMs)
            .ThenBy(check => check.CheckId, StringComparer.OrdinalIgnoreCase);
    }

    private static int Score(AuditSeverity severity) => severity switch
    {
        AuditSeverity.Critical => 5,
        AuditSeverity.High => 4,
        AuditSeverity.Medium => 3,
        AuditSeverity.Low => 2,
        _ => 1,
    };
}
