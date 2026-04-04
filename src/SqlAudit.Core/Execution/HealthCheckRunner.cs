using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Models;

namespace SqlAudit.Core.Execution;

public sealed class HealthCheckRunner
{
    private readonly IReadOnlyCollection<IHealthCheck> _checks;

    public HealthCheckRunner(IEnumerable<IHealthCheck> checks)
    {
        _checks = checks.ToArray();
    }

    public async Task<HealthCheckRunResult> RunAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();
        var executions = new List<CheckExecutionResult>();

        foreach (var check in _checks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();

            try
            {
                var current = await check.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                findings.AddRange(current);
                timer.Stop();
                executions.Add(new CheckExecutionResult(
                    check.Id,
                    check.Title,
                    check.Category,
                    CheckExecutionStatus.Success,
                    timer.ElapsedMilliseconds,
                    current.Count,
                    null));
            }
            catch (Exception ex)
            {
                timer.Stop();
                findings.Add(new AuditFinding
                {
                    Id = $"CHECK-FAIL-{check.Id}",
                    Title = $"Health check failed: {check.Title}",
                    Category = "Execution",
                    Severity = AuditSeverity.High,
                    DatabaseObject = context.Snapshot.DatabaseName,
                    Description = "A health check failed before it could complete.",
                    Impact = "Some findings may be missing or incomplete.",
                    Recommendation = "Review stack details and retry the scan.",
                    ServiceWindow = ServiceWindowAdvisor.No("Diagnostic only, no schema change required."),
                    Evidence = [new FindingEvidence("Error", ex.Message)]
                });

                executions.Add(new CheckExecutionResult(
                    check.Id,
                    check.Title,
                    check.Category,
                    CheckExecutionStatus.Failed,
                    timer.ElapsedMilliseconds,
                    0,
                    ex.Message));
            }
        }

        return new HealthCheckRunResult(findings, executions);
    }
}
