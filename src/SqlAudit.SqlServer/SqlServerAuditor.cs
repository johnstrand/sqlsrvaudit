using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;

namespace SqlAudit.SqlServer;

public sealed class SqlServerAuditor
{
    private readonly SqlServerSnapshotCollector _snapshotCollector;
    private readonly IReadOnlyCollection<IHealthCheck>? _customChecks;

    public SqlServerAuditor(IEnumerable<IHealthCheck>? checks = null)
    {
        _snapshotCollector = new SqlServerSnapshotCollector();
        _customChecks = checks?.ToArray();
    }

    public async Task<AuditReport> RunAsync(
        string connectionString,
        AuditOptions? options = null,
        AuditProfile profile = AuditProfile.Deep,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshotCollector.CollectAsync(connectionString, profile, cancellationToken).ConfigureAwait(false);
        var runner = new HealthCheckRunner(_customChecks ?? SqlServerHealthChecks.Create(profile));

        var context = new HealthCheckContext
        {
            Snapshot = snapshot,
            Options = options ?? AuditProfileDefaults.For(profile)
        };

        var runResult = await runner.RunAsync(context, cancellationToken).ConfigureAwait(false);

        return new AuditReport
        {
            ServerName = snapshot.ServerName,
            DatabaseName = snapshot.DatabaseName,
            Edition = snapshot.Edition,
            ProductVersion = snapshot.ProductVersion,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Findings = runResult.Findings
                .OrderBy(f => f.Severity)
                .ThenBy(f => f.Category, StringComparer.Ordinal)
                .ThenBy(f => f.DatabaseObject, StringComparer.Ordinal)
                .ToArray(),
            CheckExecutions = runResult.CheckExecutions
                .OrderBy(c => c.CheckId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            SuppressionSummary = new SuppressionSummary(0, 0, 0, 0, runResult.Findings.Count)
        };
    }
}
