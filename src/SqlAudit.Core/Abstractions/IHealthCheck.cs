using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;

namespace SqlAudit.Core.Abstractions;

public interface IHealthCheck
{
    string Id { get; }
    string Title { get; }
    string Category { get; }
    Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken);
}
