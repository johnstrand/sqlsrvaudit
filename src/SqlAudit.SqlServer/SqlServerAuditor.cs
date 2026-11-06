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
        CancellationToken cancellationToken = default,
        int commandTimeoutSeconds = 30)
    {
        var run = await RunWithSnapshotAsync(
                connectionString,
                options,
                profile,
                excludedSchemas,
                excludedTables,
                collectionProgress: null,
                checkProgress: null,
                cancellationToken,
                commandTimeoutSeconds)
            .ConfigureAwait(false);

        return run.Report;
    }

    public async Task<SqlServerAuditRunResult> RunWithSnapshotAsync(
        string connectionString,
        AuditOptions? options = null,
        AuditProfile profile = AuditProfile.Deep,
        IReadOnlyCollection<string>? excludedSchemas = null,
        IReadOnlyCollection<string>? excludedTables = null,
        IProgress<CollectionProgress>? collectionProgress = null,
        IProgress<string>? checkProgress = null,
        CancellationToken cancellationToken = default,
        int commandTimeoutSeconds = 30)
    {
        var snapshot = await SqlServerSnapshotCollector
            .CollectAsync(connectionString, profile, excludedSchemas, excludedTables, cancellationToken, collectionProgress, commandTimeoutSeconds)
            .ConfigureAwait(false);

        return await RunFromSnapshotAsync(
                snapshot,
                options,
                profile,
                excludedSchemas,
                excludedTables,
                checkProgress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SqlServerAuditRunResult> RunFromSnapshotAsync(
        DatabaseSnapshot snapshot,
        AuditOptions? options = null,
        AuditProfile profile = AuditProfile.Deep,
        IReadOnlyCollection<string>? excludedSchemas = null,
        IReadOnlyCollection<string>? excludedTables = null,
        IProgress<string>? checkProgress = null,
        CancellationToken cancellationToken = default)
    {
        var filteredSnapshot = SqlServerSnapshotCollector.ApplyExclusions(snapshot, excludedSchemas, excludedTables);
        var runner = new HealthCheckRunner(customChecks ?? SqlServerHealthChecks.Create(profile));

        var context = new HealthCheckContext
        {
            Snapshot = filteredSnapshot,
            Options = options ?? AuditProfileDefaults.For(profile),
        };

        var runResult = await runner.RunAsync(context, cancellationToken, checkProgress).ConfigureAwait(false);
        var excludedSchemaList = excludedSchemas?.ToArray() ?? [];
        var excludedTableList = excludedTables?.ToArray() ?? [];

        var report = new AuditReport
        {
            ServerName = filteredSnapshot.ServerName,
            DatabaseName = filteredSnapshot.DatabaseName,
            Edition = filteredSnapshot.Edition,
            ProductVersion = filteredSnapshot.ProductVersion,
            CapturedAtUtc = filteredSnapshot.CapturedAtUtc,
            ExcludedSchemas = excludedSchemaList,
            ExcludedTables = excludedTableList,
            TopResourceIntensiveQueries = filteredSnapshot.TopResourceIntensiveQueries,
            TopWaitStats = filteredSnapshot.TopWaitStats,
            QueryStoreRegressions = filteredSnapshot.QueryStoreRegressions,
            ActiveBlockingSessions = filteredSnapshot.ActiveBlockingSessions,
            DeadlockSummary = filteredSnapshot.DeadlockSummary,
            MissingIndexSignals = filteredSnapshot.MissingIndexSignals,
            LogHealth = filteredSnapshot.LogHealth,
            TempDbPressure = filteredSnapshot.TempDbPressure,
            FileGrowthHealth = filteredSnapshot.FileGrowthHealth,
            BackupPosture = filteredSnapshot.BackupPosture,
            SecurityHygieneIssues = filteredSnapshot.SecurityHygieneIssues,
            CollectionWarnings = filteredSnapshot.CollectionWarnings,
            ServerConfigurations = filteredSnapshot.ServerConfigurations,
            LastDbccCheckDbUtc = filteredSnapshot.LastDbccCheckDbUtc,
            TempDbConfig = filteredSnapshot.TempDbConfig,
            SleepingTransactions = filteredSnapshot.SleepingTransactions,
            MemoryPressure = filteredSnapshot.MemoryPressure,
            FileIoLatency = filteredSnapshot.FileIoLatency,
            PlanCache = filteredSnapshot.PlanCache,
            TableCompression = filteredSnapshot.TableCompression,
            DatabaseOptions = filteredSnapshot.DatabaseOptions,
            VolumeStats = filteredSnapshot.VolumeStats,
            FailedAgentJobs = filteredSnapshot.FailedAgentJobs,
            GlobalTraceFlags = filteredSnapshot.GlobalTraceFlags,
            Findings = [.. runResult.Findings
                .OrderBy(f => f.Severity)
                .ThenBy(f => f.Category, StringComparer.Ordinal)
                .ThenBy(f => f.DatabaseObject, StringComparer.Ordinal),],
            CheckExecutions = [.. runResult.CheckExecutions.OrderBy(c => c.CheckId, StringComparer.OrdinalIgnoreCase)],
            SuppressionSummary = new SuppressionSummary(0, 0, 0, 0, runResult.Findings.Count),
        };

        return new SqlServerAuditRunResult(filteredSnapshot, report);
    }
}
