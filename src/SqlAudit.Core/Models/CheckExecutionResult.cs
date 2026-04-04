namespace SqlAudit.Core.Models;

public enum CheckExecutionStatus
{
    Success,
    Failed,
    Skipped
}

public sealed record CheckExecutionResult(
    string CheckId,
    string Title,
    string Category,
    CheckExecutionStatus Status,
    long DurationMs,
    int FindingCount,
    string? ErrorMessage);
