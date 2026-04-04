using SqlAudit.Core.Models;

namespace SqlAudit.Core.Execution;

public sealed class HealthCheckContext
{
    public required DatabaseSnapshot Snapshot { get; init; }

    public required AuditOptions Options { get; init; }
}
