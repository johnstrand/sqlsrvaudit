namespace SqlAudit.Core.Models;

/// <summary>
/// Represents an individual issue or recommendation discovered during the audit process.
/// </summary>
public sealed class AuditFinding
{
    /// <summary>
    /// The unique rule identifier (e.g., IDX-001).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// A brief, human-readable summary of the finding.
    /// </summary>
    public required string Title { get; init; }

    public required string Category { get; init; }

    public required AuditSeverity Severity { get; init; }

    public required string DatabaseObject { get; init; }

    public required string Description { get; init; }

    public required string Impact { get; init; }

    public required string Recommendation { get; init; }

    public required ServiceWindowDecision ServiceWindow { get; init; }

    public string? FixScript { get; init; }

    public IReadOnlyList<FindingEvidence> Evidence { get; init; } = [];
}

public sealed record FindingEvidence(string Name, string Value);

public enum AuditSeverity
{
    Critical = 0,
    High = 1,
    Medium = 2,
    Low = 3,
    Info = 4,
}
