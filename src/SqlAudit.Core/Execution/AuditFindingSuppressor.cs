using System;
using System.Collections.Generic;
using System.Linq;
using SqlAudit.Core.Models;

namespace SqlAudit.Core.Execution;

public static class AuditFindingSuppressor
{
    public static SuppressionOutcome Apply(
        IReadOnlyList<AuditFinding> findings,
        IReadOnlyList<AuditSuppressionRule> rules,
        DateTimeOffset nowUtc)
    {
        if (findings.Count == 0)
        {
            return new SuppressionOutcome(
                findings,
                new SuppressionSummary(rules.Count, 0, rules.Count, 0, 0));
        }

        if (rules.Count == 0)
        {
            return new SuppressionOutcome(
                findings,
                new SuppressionSummary(0, 0, 0, 0, findings.Count));
        }

        var expiredRules = rules.Count(r => r.ExpiresUtc.HasValue && r.ExpiresUtc.Value <= nowUtc);
        var activeRules = rules
            .Where(r => !r.ExpiresUtc.HasValue || r.ExpiresUtc.Value > nowUtc)
            .ToArray();

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
            if (!string.Equals(rule.FindingId, finding.Id, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(rule.FindingId, finding.Id.Split('-', 2)[0], StringComparison.OrdinalIgnoreCase)
                && !finding.Id.StartsWith(rule.FindingId, StringComparison.OrdinalIgnoreCase))
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
}

internal static class WildcardMatcher
{
    public static bool IsMatch(string input, string pattern)
    {
        var text = input ?? string.Empty;
        var wildcard = pattern ?? string.Empty;

        var tLen = text.Length;
        var pLen = wildcard.Length;
        var dp = new bool[tLen + 1, pLen + 1];
        dp[0, 0] = true;

        for (var p = 1; p <= pLen; p++)
        {
            if (wildcard[p - 1] == '*')
            {
                dp[0, p] = dp[0, p - 1];
            }
        }

        for (var t = 1; t <= tLen; t++)
        {
            for (var p = 1; p <= pLen; p++)
            {
                var pc = wildcard[p - 1];
                if (pc == '*')
                {
                    dp[t, p] = dp[t, p - 1] || dp[t - 1, p];
                }
                else if (pc == '?' || CharsEqual(text[t - 1], pc))
                {
                    dp[t, p] = dp[t - 1, p - 1];
                }
            }
        }

        return dp[tLen, pLen];
    }

    private static bool CharsEqual(char left, char right) =>
        char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
}
