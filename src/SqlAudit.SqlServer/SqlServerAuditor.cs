using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;

namespace SqlAudit.SqlServer;

public sealed record SqlServerAuditRunResult(DatabaseSnapshot Snapshot, AuditReport Report);

public sealed class SqlServerAuditor(IEnumerable<IHealthCheck>? checks = null)
{
    private readonly IReadOnlyCollection<IHealthCheck>? customChecks = checks?.ToArray();

    public async Task<AuditReport> RunAsync(
        string connectionString,
        AuditOptions? options = null,
        AuditProfile profile = AuditProfile.Deep,
        IReadOnlyCollection<string>? excludedSchemas = null,
        IReadOnlyCollection<string>? excludedTables = null,
        CancellationToken cancellationToken = default)
    {
        var run = await RunWithSnapshotAsync(
                connectionString,
                options,
                profile,
                excludedSchemas,
                excludedTables,
                cancellationToken)
            .ConfigureAwait(false);

        return run.Report;
    }

    public async Task<SqlServerAuditRunResult> RunWithSnapshotAsync(
        string connectionString,
        AuditOptions? options = null,
        AuditProfile profile = AuditProfile.Deep,
        IReadOnlyCollection<string>? excludedSchemas = null,
        IReadOnlyCollection<string>? excludedTables = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await SqlServerSnapshotCollector
            .CollectAsync(connectionString, profile, excludedSchemas, excludedTables, cancellationToken)
            .ConfigureAwait(false);
        var runner = new HealthCheckRunner(customChecks ?? SqlServerHealthChecks.Create(profile));

        var context = new HealthCheckContext
        {
            Snapshot = snapshot,
            Options = options ?? AuditProfileDefaults.For(profile),
        };

        var runResult = await runner.RunAsync(context, cancellationToken).ConfigureAwait(false);
        var excludedSchemaList = excludedSchemas?.ToArray() ?? [];
        var excludedTableList = excludedTables?.ToArray() ?? [];

        var report = new AuditReport
        {
            ServerName = snapshot.ServerName,
            DatabaseName = snapshot.DatabaseName,
            Edition = snapshot.Edition,
            ProductVersion = snapshot.ProductVersion,
            CapturedAtUtc = snapshot.CapturedAtUtc,
            ExcludedSchemas = excludedSchemaList,
            ExcludedTables = excludedTableList,
            TopResourceIntensiveQueries = snapshot.TopResourceIntensiveQueries,
            TopWaitStats = snapshot.TopWaitStats,
            QueryStoreRegressions = snapshot.QueryStoreRegressions,
            ActiveBlockingSessions = snapshot.ActiveBlockingSessions,
            DeadlockSummary = snapshot.DeadlockSummary,
            MissingIndexSignals = snapshot.MissingIndexSignals,
            LogHealth = snapshot.LogHealth,
            TempDbPressure = snapshot.TempDbPressure,
            FileGrowthHealth = snapshot.FileGrowthHealth,
            BackupPosture = snapshot.BackupPosture,
            SecurityHygieneIssues = snapshot.SecurityHygieneIssues,
            Findings = [.. runResult.Findings
                .OrderBy(f => f.Severity)
                .ThenBy(f => f.Category, StringComparer.Ordinal)
                .ThenBy(f => f.DatabaseObject, StringComparer.Ordinal),],
            CheckExecutions = [.. runResult.CheckExecutions.OrderBy(c => c.CheckId, StringComparer.OrdinalIgnoreCase)],
            SuppressionSummary = new SuppressionSummary(0, 0, 0, 0, runResult.Findings.Count),
        };

        return new SqlServerAuditRunResult(snapshot, report);
    }
}
