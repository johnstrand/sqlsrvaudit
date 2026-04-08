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
        WriteCollectionWarnings(sb, report);
        WriteScorecard(sb, report);
        WriteCategories(sb, report);
        WriteSuppressions(sb, report);
        WriteCheckExecution(sb, report);
        WriteQuickWins(sb, report);
        WriteTopRiskyObjects(sb, report);
        WriteTopResourceIntensiveQueries(sb, report);
        WriteTopWaitStats(sb, report);
        WriteQueryStoreRegressions(sb, report);
        WriteBlockingAndDeadlocks(sb, report);
        WriteMissingIndexSignals(sb, report);
        WriteLogHealth(sb, report);
        WriteTempDbPressure(sb, report);
        WriteFileGrowthHealth(sb, report);
        WriteBackupPosture(sb, report);
        WriteIntegrityCheckHistory(sb, report);
        WriteSecurityHygiene(sb, report);
        WriteInstanceConfiguration(sb, report);
        WriteMemoryPressure(sb, report);
        WriteFileIoLatency(sb, report);
        WritePlanCacheHealth(sb, report);
        WriteSleepingTransactions(sb, report);
        WriteGrowthForecasts(sb, report);
        WriteDatabaseOptions(sb, report);
        WriteVolumeStats(sb, report);
        WriteFailedAgentJobs(sb, report);
        WriteGlobalTraceFlags(sb, report);
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

    private static void WriteCollectionWarnings(StringBuilder sb, AuditReport report)
    {
        if (report.CollectionWarnings.Count == 0)
        {
            return;
        }

        sb.AppendLine("### ⚠ Data Collection Warnings");
        sb.AppendLine();
        sb.AppendLine("Some data could not be collected during this scan. Affected sections may be empty or incomplete.");
        sb.AppendLine();
        sb.AppendLine("| Section | Reason |");
        sb.AppendLine("|---|---|");

        foreach (var warning in report.CollectionWarnings)
        {
            sb.AppendLine($"| {EscapeInline(warning.Section)} | {EscapeInline(warning.Reason)} |");
        }

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

    private static void WriteTopResourceIntensiveQueries(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Top Resource-Intensive Queries");
        sb.AppendLine();

        var topQueries = report.TopResourceIntensiveQueries
            .OrderByDescending(query => query.TotalCpuMs)
            .ThenByDescending(query => query.TotalLogicalReads)
            .ThenBy(query => query.QueryHash, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

        if (topQueries.Length == 0)
        {
            sb.AppendLine("No query runtime telemetry available.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Query Hash | Executions | Total CPU (ms) | Avg CPU (ms) | Total Reads | Last Exec (UTC) |");
        sb.AppendLine("|---|---:|---:|---:|---:|---|");

        foreach (var query in topQueries)
        {
            var lastExecution = query.LastExecutionUtc?.ToString("u", System.Globalization.CultureInfo.InvariantCulture) ?? "(n/a)";
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"| `{EscapeInline(query.QueryHash)}` | {query.ExecutionCount} | {query.TotalCpuMs:F2} | {query.AverageCpuMs:F2} | {query.TotalLogicalReads} | {EscapeInline(lastExecution)} |");
        }

        sb.AppendLine();
        sb.AppendLine("Query text snippets:");

        foreach (var query in topQueries)
        {
            var queryText = string.IsNullOrWhiteSpace(query.QueryText)
                ? "(statement text unavailable)"
                : query.QueryText;
            sb.AppendLine($"- `{EscapeInline(query.QueryHash)}` {EscapeInline(queryText)}");
        }

        sb.AppendLine();
    }

    private static void WriteTopWaitStats(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Wait Stats Breakdown");
        sb.AppendLine();

        if (report.TopWaitStats.Count == 0)
        {
            sb.AppendLine("No wait-stat telemetry available.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Wait Type | Category | Wait (s) | Signal (s) | Tasks | Avg Wait (ms) |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|");

        foreach (var wait in report.TopWaitStats.Take(12))
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"| `{EscapeInline(wait.WaitType)}` | {EscapeInline(wait.Category)} | {wait.WaitTimeSeconds:F2} | {wait.SignalWaitSeconds:F2} | {wait.WaitingTasksCount} | {wait.AverageWaitMs:F2} |");
        }

        sb.AppendLine();
    }

    private static void WriteQueryStoreRegressions(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Query Store Regressions");
        sb.AppendLine();

        if (report.QueryStoreRegressions.Count == 0)
        {
            sb.AppendLine("No significant Query Store regressions detected.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Query Id | Baseline Avg (ms) | Recent Avg (ms) | Ratio | Recent Execs |");
        sb.AppendLine("|---:|---:|---:|---:|---:|");

        foreach (var regression in report.QueryStoreRegressions.Take(10))
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"| {regression.QueryId} | {regression.BaselineAverageDurationMs:F2} | {regression.RecentAverageDurationMs:F2} | {regression.RegressionRatio:F2}x | {regression.RecentExecutions} |");
        }

        sb.AppendLine();
    }

    private static void WriteBlockingAndDeadlocks(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Blocking and Deadlocks");
        sb.AppendLine();

        if (report.DeadlockSummary is null)
        {
            sb.AppendLine("- Deadlocks (24h): (unavailable)");
        }
        else
        {
            var deadlockLastSeen = report.DeadlockSummary.LastDeadlockUtc?.ToString("u", System.Globalization.CultureInfo.InvariantCulture) ?? "(n/a)";
            sb.AppendLine($"- Deadlocks (24h): {report.DeadlockSummary.DeadlockCountLast24Hours}");
            sb.AppendLine($"- Last deadlock (UTC): {EscapeInline(deadlockLastSeen)}");
        }

        sb.AppendLine();

        if (report.ActiveBlockingSessions.Count == 0)
        {
            sb.AppendLine("No active blocking chains observed at capture time.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Blocking SPID | Blocked SPID | Wait Type | Wait (ms) | Resource |");
        sb.AppendLine("|---:|---:|---|---:|---|");

        foreach (var block in report.ActiveBlockingSessions.Take(10))
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"| {block.BlockingSessionId} | {block.BlockedSessionId} | {EscapeInline(block.WaitType)} | {block.WaitDurationMs} | {EscapeInline(block.WaitResource)} |");
        }

        sb.AppendLine();
    }

    private static void WriteMissingIndexSignals(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Missing Index Signals");
        sb.AppendLine();

        if (report.MissingIndexSignals.Count == 0)
        {
            sb.AppendLine("No high-confidence missing-index signals found.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Table | Seeks+Scans | Est. Benefit | Existing Indexes | Guardrail |");
        sb.AppendLine("|---|---:|---:|---:|---|");

        foreach (var signal in report.MissingIndexSignals.Take(15))
        {
            var table = $"[{signal.SchemaName}].[{signal.TableName}]";
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"| `{EscapeInline(table)}` | {signal.UserSeeks + signal.UserScans} | {signal.EstimatedBenefit:F2} | {signal.ExistingIndexCount} | {EscapeInline(signal.GuardrailNote)} |");
        }

        sb.AppendLine();
    }

    private static void WriteLogHealth(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Log Health");
        sb.AppendLine();

        if (report.LogHealth is null)
        {
            sb.AppendLine("No transaction-log health telemetry available.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- Total log size (MB): {report.LogHealth.TotalLogSizeMb:F2}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- Used log size (MB): {report.LogHealth.UsedLogSizeMb:F2}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- Used log (%): {report.LogHealth.UsedLogPercent:F2}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- VLF count: {report.LogHealth.VlfCount}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- Longest active transaction (min): {report.LogHealth.LongestActiveTransactionMinutes}");
        sb.AppendLine($"- Log reuse wait: `{EscapeInline(report.LogHealth.LogReuseWaitDescription)}`");
        sb.AppendLine();
    }

    private static void WriteTempDbPressure(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Tempdb Pressure");
        sb.AppendLine();

        if (report.TempDbPressure is null)
        {
            sb.AppendLine("No tempdb pressure telemetry available.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- Version store (MB): {report.TempDbPressure.VersionStoreMb:F2}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- User objects (MB): {report.TempDbPressure.UserObjectMb:F2}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- Internal objects (MB): {report.TempDbPressure.InternalObjectMb:F2}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- Unallocated (MB): {report.TempDbPressure.UnallocatedMb:F2}");
        sb.AppendLine();
    }

    private static void WriteFileGrowthHealth(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### File Growth Health");
        sb.AppendLine();

        if (report.FileGrowthHealth.Count == 0)
        {
            sb.AppendLine("No file growth telemetry available.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| File | Type | Size (MB) | Growth | Advisory |");
        sb.AppendLine("|---|---|---:|---|---|");

        foreach (var file in report.FileGrowthHealth.Take(20))
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"| `{EscapeInline(file.LogicalName)}` | {EscapeInline(file.FileType)} | {file.SizeMb:F2} | {EscapeInline(file.GrowthDescription)} | {EscapeInline(file.Advisory)} |");
        }

        sb.AppendLine();
    }

    private static void WriteBackupPosture(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Backup and Restore Posture");
        sb.AppendLine();

        if (report.BackupPosture is null)
        {
            sb.AppendLine("No backup posture telemetry available.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"- Recovery model: `{EscapeInline(report.BackupPosture.RecoveryModel)}`");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- Last full backup age (hours): {FormatNullableDecimal(report.BackupPosture.FullBackupAgeHours)}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- Last diff backup age (hours): {FormatNullableDecimal(report.BackupPosture.DifferentialBackupAgeHours)}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- Last log backup age (hours): {FormatNullableDecimal(report.BackupPosture.LogBackupAgeHours)}");
        sb.AppendLine();
    }

    private static void WriteSecurityHygiene(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Security Hygiene");
        sb.AppendLine();

        if (report.SecurityHygieneIssues.Count == 0)
        {
            sb.AppendLine("No obvious security hygiene issues were detected.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Severity | Issue | Principal | Details |");
        sb.AppendLine("|---|---|---|---|");

        foreach (var issue in report.SecurityHygieneIssues.Take(25))
        {
            sb.AppendLine($"| {issue.Severity} | {EscapeInline(issue.IssueType)} | `{EscapeInline(issue.Principal)}` | {EscapeInline(issue.Details)} |");
        }

        sb.AppendLine();
    }

    private static void WriteGrowthForecasts(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Growth Forecasting");
        sb.AppendLine();

        if (report.TableGrowthForecasts.Count == 0)
        {
            sb.AppendLine("No multi-run growth forecast available yet (or growth delta is below threshold).");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Object | Delta (MB) | Days | Projected 30d (MB) | Projected 90d (MB) |");
        sb.AppendLine("|---|---:|---:|---:|---:|");

        foreach (var forecast in report.TableGrowthForecasts.Take(15))
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"| `{EscapeInline(forecast.DatabaseObject)}` | {forecast.DeltaReservedMb:F2} | {forecast.ElapsedDays:F1} | {forecast.Projected30DayReservedMb:F2} | {forecast.Projected90DayReservedMb:F2} |");
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

    private static string FormatNullableDecimal(decimal? value)
    {
        return value.HasValue
            ? value.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
            : "(n/a)";
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

    private static void WriteIntegrityCheckHistory(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Integrity Check History (DBCC CHECKDB)");
        sb.AppendLine();
        if (report.LastDbccCheckDbUtc is null)
        {
            sb.AppendLine("Last DBCC CHECKDB timestamp is unavailable (not yet run or permission denied).");
        }
        else
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"- Last DBCC CHECKDB (UTC): `{report.LastDbccCheckDbUtc.Value:u}`");
        }

        sb.AppendLine();
    }

    private static void WriteInstanceConfiguration(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Instance Configuration");
        sb.AppendLine();

        if (report.ServerConfigurations.Count == 0)
        {
            sb.AppendLine("Server configuration data not available.");
            sb.AppendLine();
            return;
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "max degree of parallelism",
            "cost threshold for parallelism",
            "optimize for ad hoc workloads",
            "max server memory (MB)",
            "min server memory (MB)",
            "blocked process threshold (s)",
        };

        sb.AppendLine("| Setting | Value |");
        sb.AppendLine("|---|---:|");

        foreach (var cfg in report.ServerConfigurations.Where(c => keys.Contains(c.Name)))
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"| `{EscapeInline(cfg.Name)}` | {cfg.ValueInUse:G} |");
        }

        sb.AppendLine();
    }

    private static void WriteMemoryPressure(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Memory Pressure");
        sb.AppendLine();

        if (report.MemoryPressure is null)
        {
            sb.AppendLine("Memory pressure telemetry not available (VIEW SERVER STATE may be required).");
            sb.AppendLine();
            return;
        }

        var mem = report.MemoryPressure;
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"- Page Life Expectancy: **{mem.PageLifeExpectancySeconds:N0}s** (target: ≥300s)");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"- Buffer Cache Hit Ratio: {mem.BufferCacheHitRatioPercent:F1}%");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"- Total Server Memory: {mem.TotalServerMemoryMb:F0} MB");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"- Target Server Memory: {mem.TargetServerMemoryMb:F0} MB");
        sb.AppendLine();
    }

    private static void WriteFileIoLatency(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### File I/O Latency");
        sb.AppendLine();

        if (report.FileIoLatency.Count == 0)
        {
            sb.AppendLine("File I/O latency data not available (VIEW SERVER STATE may be required).");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| DB | File | Type | Size (MB) | Avg Read (ms) | Avg Write (ms) |");
        sb.AppendLine("|---|---|---|---:|---:|---:|");

        foreach (var file in report.FileIoLatency.OrderByDescending(f => Math.Max(f.AvgReadLatencyMs, f.AvgWriteLatencyMs)).Take(20))
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"| {file.DatabaseId} | `{EscapeInline(file.LogicalName)}` | {EscapeInline(file.FileType)} | {file.SizeMb:F0} | {file.AvgReadLatencyMs:F1} | {file.AvgWriteLatencyMs:F1} |");
        }

        sb.AppendLine();
    }

    private static void WritePlanCacheHealth(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Plan Cache Health");
        sb.AppendLine();

        if (report.PlanCache is null)
        {
            sb.AppendLine("Plan cache telemetry not available (VIEW SERVER STATE may be required).");
            sb.AppendLine();
            return;
        }

        var cache = report.PlanCache;
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"- Total cached plans: {cache.TotalCachedPlans:N0}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"- Single-use plans: {cache.SingleUsePlans:N0} ({cache.SingleUsePlanPercent:F1}%)");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"- Total cache size: {cache.CacheSizeMb:F0} MB");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
            $"- Ad hoc cache size: {cache.AdHocCacheSizeMb:F0} MB");
        sb.AppendLine();
    }

    private static void WriteSleepingTransactions(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Sleeping Sessions with Open Transactions");
        sb.AppendLine();

        if (report.SleepingTransactions.Count == 0)
        {
            sb.AppendLine("No sleeping sessions with open transactions detected.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Session | Login | Database | Open Txns | Elapsed (min) | Last Query |");
        sb.AppendLine("|---|---|---|---:|---:|---|");

        foreach (var s in report.SleepingTransactions.Take(15))
        {
            var queryPreview = s.LastQueryText.Length > 60
                ? string.Concat(s.LastQueryText.AsSpan(0, 60), "...")
                : s.LastQueryText;
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"| {s.SessionId} | `{EscapeInline(s.LoginName)}` | `{EscapeInline(s.DatabaseName)}` | {s.OpenTransactionCount} | {s.ElapsedMinutes:F1} | `{EscapeInline(queryPreview)}` |");
        }

        sb.AppendLine();
    }

    private static void WriteDatabaseOptions(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Database Options");
        sb.AppendLine();

        var opts = report.DatabaseOptions;
        if (opts is null)
        {
            sb.AppendLine("Database options data was not collected.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Option | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| AUTO_SHRINK | `{(opts.AutoShrink ? "ON ⚠️" : "OFF")}` |");
        sb.AppendLine($"| AUTO_CLOSE | `{(opts.AutoClose ? "ON ⚠️" : "OFF")}` |");
        sb.AppendLine($"| PAGE_VERIFY | `{EscapeInline(opts.PageVerify)}` |");
        sb.AppendLine($"| READ_COMMITTED_SNAPSHOT | `{(opts.IsRcsiEnabled ? "ON" : "OFF")}` |");
        sb.AppendLine($"| QUERY_STORE | `{(opts.QueryStoreEnabled ? "ON" : "OFF")}` |");
        if (opts.QueryStoreEnabled)
        {
            sb.AppendLine($"| QUERY_STORE_STATE | `{EscapeInline(opts.QueryStoreState)}` |");
        }

        sb.AppendLine();
    }

    private static void WriteVolumeStats(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Storage Volumes");
        sb.AppendLine();

        if (report.VolumeStats.Count == 0)
        {
            sb.AppendLine("Volume statistics data was not collected (may require VIEW SERVER STATE permission).");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Volume | File | Type | Available GB | Total GB | Available % |");
        sb.AppendLine("|---|---|---|---:|---:|---:|");

        foreach (var v in report.VolumeStats.OrderBy(v => v.VolumeMount, StringComparer.OrdinalIgnoreCase))
        {
            var totalGb = v.TotalBytes / (1024m * 1024 * 1024);
            var availGb = v.AvailableBytes / (1024m * 1024 * 1024);
            string warning;
            if (v.AvailablePercent < 5m)
            {
                warning = " ⛔";
            }
            else if (v.AvailablePercent < 15m)
            {
                warning = " ⚠️";
            }
            else
            {
                warning = string.Empty;
            }
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"| `{EscapeInline(v.VolumeMount)}` | `{EscapeInline(v.LogicalName)}` | {v.FileType} | {availGb:F1} | {totalGb:F1} | {v.AvailablePercent:F1}%{warning} |");
        }

        sb.AppendLine();
    }

    private static void WriteFailedAgentJobs(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### SQL Agent Job Failures (Last 7 Days)");
        sb.AppendLine();

        if (report.FailedAgentJobs.Count == 0)
        {
            sb.AppendLine("No SQL Agent job failures in the last 7 days (or SQL Agent data not accessible).");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Job Name | Step | Last Failed (UTC) | Error |");
        sb.AppendLine("|---|---|---|---|");

        foreach (var job in report.FailedAgentJobs.Take(20))
        {
            var errPreview = job.ErrorMessage.Length > 80
                ? string.Concat(job.ErrorMessage.AsSpan(0, 80), "…")
                : job.ErrorMessage;
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"| `{EscapeInline(job.JobName)}` | `{EscapeInline(job.StepName)}` | {job.LastRunUtc:u} | {EscapeInline(errPreview)} |");
        }

        sb.AppendLine();
    }

    private static void WriteGlobalTraceFlags(StringBuilder sb, AuditReport report)
    {
        sb.AppendLine("### Active Global Trace Flags");
        sb.AppendLine();

        var globalFlags = report.GlobalTraceFlags.Where(f => f.IsGlobal).ToArray();
        if (globalFlags.Length == 0)
        {
            sb.AppendLine("No global trace flags are enabled.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Trace Flag | Global |");
        sb.AppendLine("|---:|---|");

        foreach (var flag in globalFlags.OrderBy(f => f.TraceFlag))
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                $"| {flag.TraceFlag} | Yes |");
        }

        sb.AppendLine();
    }
}
