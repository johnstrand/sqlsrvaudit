using SqlAudit.Core.Models;

namespace SqlAudit.Core.Execution;

public sealed record HealthCheckRunResult(
    IReadOnlyCollection<AuditFinding> Findings,
    IReadOnlyCollection<CheckExecutionResult> CheckExecutions);
