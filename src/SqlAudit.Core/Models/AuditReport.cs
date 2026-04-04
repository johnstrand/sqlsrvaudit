namespace SqlAudit.Core.Models;

public sealed class AuditReport
{
    public string SchemaVersion { get; init; } = "1.1";

    public required string ServerName { get; init; }

    public required string DatabaseName { get; init; }

    public required string Edition { get; init; }

    public required string ProductVersion { get; init; }

    public required DateTimeOffset CapturedAtUtc { get; init; }

    public required IReadOnlyList<AuditFinding> Findings { get; init; }

    public IReadOnlyList<CheckExecutionResult> CheckExecutions { get; init; } = [];

    public SuppressionSummary SuppressionSummary { get; init; } = SuppressionSummary.None;

    public IReadOnlyDictionary<AuditSeverity, int> SeverityCounts => Findings
        .GroupBy(f => f.Severity)
        .ToDictionary(g => g.Key, g => g.Count());

    public IReadOnlyDictionary<string, int> CategoryCounts => Findings
        .GroupBy(f => f.Category, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
}
