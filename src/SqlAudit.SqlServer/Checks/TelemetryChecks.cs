using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;
using System.Globalization;

namespace SqlAudit.SqlServer.Checks;

internal sealed class FileGrowthHealthCheck : IHealthCheck
{
    public string Id => "FILE-001";

    public string Title => "Suboptimal database file growth or capacity risk";

    public string Category => "Storage";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var files = context.Snapshot.FileGrowthHealth;
        if (files.Count == 0)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var findings = new List<AuditFinding>();
        var dbName = context.Snapshot.DatabaseName.Replace("]", "]]", StringComparison.Ordinal);

        foreach (var file in files)
        {
            var escapedLogical = file.LogicalName.Replace("'", "''", StringComparison.Ordinal);
            if (file.MaxSizeMb is > 0m && file.SizeMb / file.MaxSizeMb.Value >= 0.85m)
            {
                var usedPct = Math.Round((file.SizeMb / file.MaxSizeMb.Value) * 100m, 1);
                var severity = usedPct >= 95m ? AuditSeverity.High : AuditSeverity.Medium;
                findings.Add(new AuditFinding
                {
                    Id = $"FILE-001-MAXSIZE-{file.FileId}",
                    Title = $"Database file '{file.LogicalName}' is approaching MAXSIZE",
                    Category = Category,
                    Severity = severity,
                    DatabaseObject = file.LogicalName,
                    Description = $"File '{file.LogicalName}' ({file.FileType}) is {file.SizeMb.ToString("F1", CultureInfo.InvariantCulture)} MB out of a configured MAXSIZE of {file.MaxSizeMb.Value.ToString("F1", CultureInfo.InvariantCulture)} MB ({usedPct.ToString("F1", CultureInfo.InvariantCulture)}% full).",
                    Impact = "Once the file reaches MAXSIZE, further allocations will fail and transactions against the database will abort.",
                    Recommendation = "Increase MAXSIZE (or set to UNLIMITED if underlying storage is monitored) or archive data before capacity is exhausted.",
                    ServiceWindow = ServiceWindowAdvisor.No("Raising file MAXSIZE via ALTER DATABASE ... MODIFY FILE is an online metadata operation."),
                    FixScript = $"-- Review available disk space before raising MAXSIZE\nALTER DATABASE [{dbName}] MODIFY FILE (NAME = N'{escapedLogical}', MAXSIZE = UNLIMITED);",
                    Evidence =
                    [
                        new FindingEvidence("LogicalName", file.LogicalName),
                        new FindingEvidence("FileType", file.FileType),
                        new FindingEvidence("SizeMb", file.SizeMb.ToString("F1", CultureInfo.InvariantCulture)),
                        new FindingEvidence("MaxSizeMb", file.MaxSizeMb.Value.ToString("F1", CultureInfo.InvariantCulture)),
                        new FindingEvidence("PercentOfMaxSize", usedPct.ToString("F1", CultureInfo.InvariantCulture) + "%"),
                    ],
                });
            }

            if (file.IsPercentGrowth && file.GrowthValue > 0m)
            {
                var recommendedGrowthMb = string.Equals(file.FileType, "LOG", StringComparison.OrdinalIgnoreCase) ? 256 : 512;
                findings.Add(new AuditFinding
                {
                    Id = $"FILE-001-PERCENT-{file.FileId}",
                    Title = $"Database file '{file.LogicalName}' uses percentage autogrowth",
                    Category = Category,
                    Severity = AuditSeverity.Medium,
                    DatabaseObject = file.LogicalName,
                    Description = $"File '{file.LogicalName}' ({file.FileType}, {file.SizeMb.ToString("F1", CultureInfo.InvariantCulture)} MB) is configured with {file.GrowthDescription} percentage autogrowth.",
                    Impact = "Percentage growth produces increasingly large autogrowth increments as the file grows (causing long I/O stalls, especially on transaction log files where zero-initialization is always required) or tiny increments on small files.",
                    Recommendation = $"Switch '{file.LogicalName}' from percentage autogrowth to a fixed MB increment (e.g., {recommendedGrowthMb} MB).",
                    ServiceWindow = ServiceWindowAdvisor.No("Changing FILEGROWTH with ALTER DATABASE ... MODIFY FILE is safe to apply online without downtime."),
                    FixScript = $"ALTER DATABASE [{dbName}] MODIFY FILE (NAME = N'{escapedLogical}', FILEGROWTH = {recommendedGrowthMb}MB);",
                    Evidence =
                    [
                        new FindingEvidence("LogicalName", file.LogicalName),
                        new FindingEvidence("FileType", file.FileType),
                        new FindingEvidence("SizeMb", file.SizeMb.ToString("F1", CultureInfo.InvariantCulture)),
                        new FindingEvidence("Growth", file.GrowthDescription),
                    ],
                });
            }
            else if (!file.IsPercentGrowth && file.GrowthValue > 0m && file.GrowthValue < 64m)
            {
                var recommendedGrowthMb = string.Equals(file.FileType, "LOG", StringComparison.OrdinalIgnoreCase) ? 256 : 512;
                findings.Add(new AuditFinding
                {
                    Id = $"FILE-001-SMALLGROWTH-{file.FileId}",
                    Title = $"Database file '{file.LogicalName}' has a small autogrowth increment ({file.GrowthDescription})",
                    Category = Category,
                    Severity = AuditSeverity.Low,
                    DatabaseObject = file.LogicalName,
                    Description = $"File '{file.LogicalName}' ({file.FileType}, {file.SizeMb.ToString("F1", CultureInfo.InvariantCulture)} MB) has a fixed autogrowth increment of {file.GrowthDescription}, which is below the recommended 64 MB minimum.",
                    Impact = "Small autogrowth increments cause frequent file growth events, physical fragmentation, and high VLF counts on transaction log files.",
                    Recommendation = $"Increase the autogrowth increment for '{file.LogicalName}' to at least 64 MB (e.g., {recommendedGrowthMb} MB).",
                    ServiceWindow = ServiceWindowAdvisor.No("Changing FILEGROWTH with ALTER DATABASE ... MODIFY FILE is safe to apply online without downtime."),
                    FixScript = $"ALTER DATABASE [{dbName}] MODIFY FILE (NAME = N'{escapedLogical}', FILEGROWTH = {recommendedGrowthMb}MB);",
                    Evidence =
                    [
                        new FindingEvidence("LogicalName", file.LogicalName),
                        new FindingEvidence("FileType", file.FileType),
                        new FindingEvidence("SizeMb", file.SizeMb.ToString("F1", CultureInfo.InvariantCulture)),
                        new FindingEvidence("Growth", file.GrowthDescription),
                    ],
                });
            }
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class QueryStoreRegressionCheck : IHealthCheck
{
    public string Id => "QRY-001";

    public string Title => "Query Store plan duration regression detected";

    public string Category => "Performance";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var regressions = context.Snapshot.QueryStoreRegressions;
        if (regressions.Count == 0)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var findings = new List<AuditFinding>(regressions.Count);
        foreach (var reg in regressions)
        {
            var severity = reg.RegressionRatio >= 3.0m ? AuditSeverity.High : AuditSeverity.Medium;
            var snippet = reg.QueryText.Length > 120 ? reg.QueryText[..120] + "..." : reg.QueryText;

            findings.Add(new AuditFinding
            {
                Id = $"QRY-001-{reg.QueryId}",
                Title = $"Query Store query {reg.QueryId} regressed by {reg.RegressionRatio.ToString("F2", CultureInfo.InvariantCulture)}x",
                Category = Category,
                Severity = severity,
                DatabaseObject = $"QueryId:{reg.QueryId}",
                Description = $"Query {reg.QueryId} average duration increased from {reg.BaselineAverageDurationMs.ToString("F2", CultureInfo.InvariantCulture)} ms (baseline) to {reg.RecentAverageDurationMs.ToString("F2", CultureInfo.InvariantCulture)} ms over {reg.RecentExecutions} recent executions ({reg.RegressionRatio.ToString("F2", CultureInfo.InvariantCulture)}x regression). Statement: {snippet}",
                Impact = "Plan regressions increase query latency, CPU consumption, and lock hold times for affected workloads.",
                Recommendation = $"Inspect QueryId {reg.QueryId} in sys.query_store_plan. If caused by parameter sniffing or a suboptimal plan change, consider forcing the faster baseline plan via sys.sp_query_store_force_plan or updating statistics.",
                ServiceWindow = ServiceWindowAdvisor.No("Forcing a baseline plan in Query Store or updating statistics does not require a maintenance window."),
                Evidence =
                [
                    new FindingEvidence("QueryId", reg.QueryId.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("BaselineAvgDurationMs", reg.BaselineAverageDurationMs.ToString("F2", CultureInfo.InvariantCulture)),
                    new FindingEvidence("RecentAvgDurationMs", reg.RecentAverageDurationMs.ToString("F2", CultureInfo.InvariantCulture)),
                    new FindingEvidence("RegressionRatio", reg.RegressionRatio.ToString("F2", CultureInfo.InvariantCulture) + "x"),
                    new FindingEvidence("RecentExecutions", reg.RecentExecutions.ToString(CultureInfo.InvariantCulture)),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class ActiveBlockingCheck : IHealthCheck
{
    private const long MinBlockingDurationMs = 5_000;
    private const long HighBlockingDurationMs = 30_000;

    public string Id => "CONC-001";

    public string Title => "Sustained active blocking sessions detected";

    public string Category => "Concurrency";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var sessions = context.Snapshot.ActiveBlockingSessions
            .Where(s => s.WaitDurationMs >= MinBlockingDurationMs)
            .ToArray();

        if (sessions.Length == 0)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var findings = new List<AuditFinding>(sessions.Length);
        foreach (var session in sessions)
        {
            var severity = session.WaitDurationMs >= HighBlockingDurationMs ? AuditSeverity.High : AuditSeverity.Medium;
            var waitSeconds = session.WaitDurationMs / 1000.0m;

            findings.Add(new AuditFinding
            {
                Id = $"CONC-001-SESSION-{session.BlockedSessionId}-BY-{session.BlockingSessionId}",
                Title = $"Session {session.BlockedSessionId} blocked by session {session.BlockingSessionId} for {waitSeconds.ToString("F1", CultureInfo.InvariantCulture)}s",
                Category = Category,
                Severity = severity,
                DatabaseObject = $"Session {session.BlockedSessionId}",
                Description = $"Session {session.BlockedSessionId} has been waiting for {waitSeconds.ToString("F1", CultureInfo.InvariantCulture)}s on {session.WaitType} (resource: '{session.WaitResource}') blocked by session {session.BlockingSessionId}.",
                Impact = "Sustained blocking causes request timeouts, connection pool exhaustion, and cascading lock queues.",
                Recommendation = "Investigate the head blocker transaction, add missing indexes to reduce scan locks, keep transactions short, or evaluate Read Committed Snapshot Isolation (RCSI).",
                ServiceWindow = ServiceWindowAdvisor.No("Investigating live blocking sessions is observational; resolving the root cause depends on the blocking query or index change."),
                Evidence =
                [
                    new FindingEvidence("BlockingSessionId", session.BlockingSessionId.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("BlockedSessionId", session.BlockedSessionId.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("WaitType", session.WaitType),
                    new FindingEvidence("WaitDurationMs", session.WaitDurationMs.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("WaitResource", session.WaitResource),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class RecentDeadlocksCheck : IHealthCheck
{
    public string Id => "CONC-002";

    public string Title => "Deadlocks detected in the last 24 hours";

    public string Category => "Concurrency";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var summary = context.Snapshot.DeadlockSummary;
        if (summary is null || summary.DeadlockCountLast24Hours <= 0)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var count = summary.DeadlockCountLast24Hours;
        var severity = count >= 5 ? AuditSeverity.High : AuditSeverity.Medium;
        var lastUtc = summary.LastDeadlockUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "unknown";

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(
        [
            new AuditFinding
            {
                Id = "CONC-002-DEADLOCKS-24H",
                Title = $"{count} deadlock(s) recorded in the last 24 hours",
                Category = Category,
                Severity = severity,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = $"The system_health Extended Events session recorded {count} deadlock(s) in the past 24 hours (most recent: {lastUtc}).",
                Impact = "Deadlocks terminate victim transactions with error 1205, causing failed requests or application retries and indicating lock-ordering or indexing issues.",
                Recommendation = "Extract the xml_deadlock_report events from the system_health session, align table access order across transactions, and add covering indexes to eliminate key lookups and range scans under lock.",
                ServiceWindow = ServiceWindowAdvisor.No("Analyzing deadlock graphs does not require downtime."),
                Evidence =
                [
                    new FindingEvidence("DeadlockCountLast24Hours", count.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("LastDeadlockUtc", lastUtc),
                ],
            },
        ]);
    }
}

internal sealed class MissingIndexSignalCheck : IHealthCheck
{
    public string Id => "IDX-012";

    public string Title => "High-impact missing index signal from DMV";

    public string Category => "Indexes";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var signals = context.Snapshot.MissingIndexSignals
            .Where(s => s.AverageUserImpactPercent >= 70m && (s.UserSeeks + s.UserScans) >= 100)
            .ToArray();

        if (signals.Length == 0)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var findings = new List<AuditFinding>(signals.Length);
        var onlineClause = context.Snapshot.SupportsOnlineIndexOperations ? " WITH (ONLINE = ON)" : string.Empty;

        var indexCounter = 0;
        foreach (var signal in signals)
        {
            indexCounter++;
            var severity = signal.EstimatedBenefit >= 100_000m && signal.ExistingIndexCount < 12
                ? AuditSeverity.Medium
                : AuditSeverity.Low;

            var keyParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(signal.EqualityColumns))
            {
                keyParts.Add(signal.EqualityColumns);
            }

            if (!string.IsNullOrWhiteSpace(signal.InequalityColumns))
            {
                keyParts.Add(signal.InequalityColumns);
            }

            var keyClause = string.Join(", ", keyParts);
            var includeClause = string.IsNullOrWhiteSpace(signal.IncludedColumns)
                ? string.Empty
                : $" INCLUDE ({signal.IncludedColumns})";

            var indexName = $"IX_{signal.TableName}_Missing_{signal.ObjectId}_{indexCounter}";
            var serviceWindow = context.Snapshot.SupportsOnlineIndexOperations
                ? ServiceWindowAdvisor.No("Edition supports ONLINE = ON index creation, though schema stability locks are briefly acquired at start and end.")
                : ServiceWindowAdvisor.Yes("Creating a nonclustered index with ONLINE = OFF holds shared table locks and blocks writes until index creation finishes.");

            findings.Add(new AuditFinding
            {
                Id = $"IDX-012-{signal.ObjectId}-{indexCounter}",
                Title = $"High-impact missing index candidate on [{signal.SchemaName}].[{signal.TableName}]",
                Category = Category,
                Severity = severity,
                DatabaseObject = $"[{signal.SchemaName}].[{signal.TableName}]",
                Description = $"Missing index DMV signals an estimated {signal.AverageUserImpactPercent.ToString("F1", CultureInfo.InvariantCulture)}% impact across {signal.UserSeeks + signal.UserScans} seeks/scans (benefit score: {signal.EstimatedBenefit.ToString("F0", CultureInfo.InvariantCulture)}) on keys ({keyClause}){includeClause}. {signal.GuardrailNote}",
                Impact = "Queries against this table are performing more expensive scans or lookups than necessary, increasing CPU and logical reads.",
                Recommendation = "Review existing indexes on the table to see if an index can be widened, or create the candidate nonclustered index after validating write overhead.",
                ServiceWindow = serviceWindow,
                FixScript = $"-- Validate against existing indexes on [{signal.SchemaName}].[{signal.TableName}] (existing index count: {signal.ExistingIndexCount}) before applying\nCREATE NONCLUSTERED INDEX [{indexName}] ON [{signal.SchemaName}].[{signal.TableName}] ({keyClause}){includeClause}{onlineClause};",
                Evidence =
                [
                    new FindingEvidence("Table", $"[{signal.SchemaName}].[{signal.TableName}]"),
                    new FindingEvidence("EqualityColumns", signal.EqualityColumns),
                    new FindingEvidence("InequalityColumns", signal.InequalityColumns),
                    new FindingEvidence("IncludedColumns", signal.IncludedColumns),
                    new FindingEvidence("UserSeeksAndScans", (signal.UserSeeks + signal.UserScans).ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("AvgUserImpactPercent", signal.AverageUserImpactPercent.ToString("F1", CultureInfo.InvariantCulture) + "%"),
                    new FindingEvidence("EstimatedBenefit", signal.EstimatedBenefit.ToString("F2", CultureInfo.InvariantCulture)),
                    new FindingEvidence("ExistingIndexCount", signal.ExistingIndexCount.ToString(CultureInfo.InvariantCulture)),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class TempDbPressureCheck : IHealthCheck
{
    private const decimal VersionStoreWarningMb = 1024m;
    private const decimal VersionStoreHighMb = 4096m;
    private const decimal LowFreeSpaceRatioWarning = 0.15m;
    private const decimal LowFreeSpaceRatioHigh = 0.05m;

    public string Id => "TMPDB-003";

    public string Title => "TempDB space or version store pressure";

    public string Category => "Configuration";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var pressure = context.Snapshot.TempDbPressure;
        if (pressure is null)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var totalMb = pressure.VersionStoreMb + pressure.UserObjectMb + pressure.InternalObjectMb + pressure.UnallocatedMb;
        var freeRatio = totalMb > 0m ? pressure.UnallocatedMb / totalMb : 1m;

        var isVersionStoreElevated = pressure.VersionStoreMb >= VersionStoreWarningMb;
        var isFreeSpaceLow = totalMb > 0m && freeRatio < LowFreeSpaceRatioWarning;

        if (!isVersionStoreElevated && !isFreeSpaceLow)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var isHigh = pressure.VersionStoreMb >= VersionStoreHighMb || (totalMb > 0m && freeRatio < LowFreeSpaceRatioHigh);
        var severity = isHigh ? AuditSeverity.High : AuditSeverity.Medium;
        var freePct = Math.Round(freeRatio * 100m, 1);

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(
        [
            new AuditFinding
            {
                Id = "TMPDB-003-SPACE-PRESSURE",
                Title = "TempDB space usage or version store is elevated",
                Category = Category,
                Severity = severity,
                DatabaseObject = "tempdb",
                Description = $"TempDB has {pressure.UnallocatedMb.ToString("F1", CultureInfo.InvariantCulture)} MB unallocated ({freePct.ToString("F1", CultureInfo.InvariantCulture)}% free of {totalMb.ToString("F1", CultureInfo.InvariantCulture)} MB total), with {pressure.VersionStoreMb.ToString("F1", CultureInfo.InvariantCulture)} MB in the version store, {pressure.UserObjectMb.ToString("F1", CultureInfo.InvariantCulture)} MB in user objects, and {pressure.InternalObjectMb.ToString("F1", CultureInfo.InvariantCulture)} MB in internal objects.",
                Impact = "If TempDB runs out of space or the version store grows unchecked due to long-running transactions, queries using sorts, hashes, temp tables, or row versioning (RCSI/Snapshot) will fail.",
                Recommendation = "Identify and resolve long-running transactions preventing version store cleanup, optimize queries with large spills or temp tables, and ensure TempDB files have sufficient capacity.",
                ServiceWindow = ServiceWindowAdvisor.No("Investigating TempDB space consumers is an online operational task."),
                Evidence =
                [
                    new FindingEvidence("VersionStoreMb", pressure.VersionStoreMb.ToString("F1", CultureInfo.InvariantCulture)),
                    new FindingEvidence("UserObjectMb", pressure.UserObjectMb.ToString("F1", CultureInfo.InvariantCulture)),
                    new FindingEvidence("InternalObjectMb", pressure.InternalObjectMb.ToString("F1", CultureInfo.InvariantCulture)),
                    new FindingEvidence("UnallocatedMb", pressure.UnallocatedMb.ToString("F1", CultureInfo.InvariantCulture)),
                    new FindingEvidence("FreeSpacePercent", freePct.ToString("F1", CultureInfo.InvariantCulture) + "%"),
                ],
            },
        ]);
    }
}
