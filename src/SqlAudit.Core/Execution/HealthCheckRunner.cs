using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Models;
using System.Diagnostics;

namespace SqlAudit.Core.Execution;

/// <summary>
/// Orchestrates the execution of multiple health checks against a given database snapshot context.
/// Collects findings and execution timing for reporting.
/// </summary>
public sealed class HealthCheckRunner(IEnumerable<IHealthCheck> checks)
{
    private readonly IReadOnlyCollection<IHealthCheck> checks = [.. checks];

    /// <summary>
    /// Executes all configured health checks asynchronously.
    /// Any check that throws an exception is recorded as a failed execution rather than crashing the runner.
    /// </summary>
    /// <param name="context">The current database and audit configuration context.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <param name="checkProgress">Optional progress reporter to track execution phase.</param>
    /// <returns>A result containing all findings and execution details.</returns>
    public async Task<HealthCheckRunResult> RunAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken,
        IProgress<string>? checkProgress = null)
    {
        var findings = new List<AuditFinding>();
        var executions = new List<CheckExecutionResult>();

        foreach (var check in checks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            checkProgress?.Report(check.Id);
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
                    ErrorMessage: null));
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
                    Evidence = [new FindingEvidence("Error", ex.Message)],
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
