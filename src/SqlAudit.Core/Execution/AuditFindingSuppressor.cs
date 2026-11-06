using System.IO.Enumeration;
using SqlAudit.Core.Models;

namespace SqlAudit.Core.Execution;

/// <summary>
/// Applies suppression rules to filter out ignored or acceptable findings from the final report.
/// </summary>
public static class AuditFindingSuppressor
{
    public static SuppressionOutcome Apply(
        IReadOnlyList<AuditFinding> findings,
        IReadOnlyList<AuditSuppressionRule> rules,
        DateTimeOffset nowUtc)
    {
        if (rules.Count == 0)
        {
            return new SuppressionOutcome(
                findings,
                new SuppressionSummary(0, 0, 0, 0, findings.Count));
        }

        var expiredRules = rules.Count(r => r.ExpiresUtc <= nowUtc);
        var activeRules = rules
            .Where(r => !r.ExpiresUtc.HasValue || r.ExpiresUtc.Value > nowUtc)
            .ToArray();

        if (findings.Count == 0)
        {
            return new SuppressionOutcome(
                findings,
                new SuppressionSummary(rules.Count, activeRules.Length, expiredRules, 0, 0));
        }

        var remaining = new List<AuditFinding>(findings.Count);
        var suppressedCount = 0;

        foreach (var finding in findings)
        {
            if (IsSuppressed(finding, activeRules))
            {
                suppressedCount++;
                continue;
            }

            remaining.Add(finding);
        }

        return new SuppressionOutcome(
            remaining,
            new SuppressionSummary(
                rules.Count,
                activeRules.Length,
                expiredRules,
                suppressedCount,
                remaining.Count));
    }

    private static bool IsSuppressed(AuditFinding finding, IReadOnlyList<AuditSuppressionRule> rules)
    {
        foreach (var rule in rules)
        {
            if (!MatchesFindingId(rule.FindingId, finding.Id))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.DatabaseObjectPattern))
            {
                return true;
            }

            if (WildcardMatcher.IsMatch(finding.DatabaseObject, rule.DatabaseObjectPattern!))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesFindingId(string ruleFindingId, string findingId)
    {
        if (string.Equals(ruleFindingId, findingId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (findingId.StartsWith(ruleFindingId, StringComparison.OrdinalIgnoreCase)
            && findingId.Length > ruleFindingId.Length
            && findingId[ruleFindingId.Length] == '-')
        {
            return true;
        }

        return false;
    }
}

internal static class WildcardMatcher
{
    public static bool IsMatch(string input, string pattern)
    {
        var text = input ?? string.Empty;
        var wildcard = pattern ?? string.Empty;

        return FileSystemName.MatchesSimpleExpression(wildcard, text, ignoreCase: true);
    }
}
