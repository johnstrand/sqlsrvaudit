using System;
using System.Collections.Generic;

namespace SqlAudit.Core.Models;

public sealed record AuditSuppressionRule(
    string FindingId,
    string? DatabaseObjectPattern,
    string? Reason,
    DateTimeOffset? ExpiresUtc);

public sealed record SuppressionSummary(
    int TotalRules,
    int ActiveRules,
    int ExpiredRules,
    int SuppressedFindings,
    int RemainingFindings)
{
    public static SuppressionSummary None { get; } = new(0, 0, 0, 0, 0);
}

public sealed record SuppressionOutcome(
    IReadOnlyList<AuditFinding> Findings,
    SuppressionSummary Summary);
