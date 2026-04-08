using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;

namespace SqlAudit.SqlServer.Checks;

internal sealed class AutoShrinkCheck : IHealthCheck
{
    public string Id => "DB-001";

    public string Title => "AUTO_SHRINK enabled";

    public string Category => "Configuration";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var opts = context.Snapshot.DatabaseOptions;
        if (opts?.AutoShrink != true)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(
        [
            new AuditFinding
            {
                Id = "DB-001-AUTO-SHRINK",
                Title = "AUTO_SHRINK is enabled",
                Category = Category,
                Severity = AuditSeverity.High,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = "AUTO_SHRINK causes SQL Server to periodically shrink data and log files when they exceed the 'shrink threshold'. This is a well-documented anti-pattern.",
                Impact = "AUTO_SHRINK causes index fragmentation by compacting data, then the database immediately regrows as data is inserted, causing repeated shrink-grow cycles that waste I/O and CPU. This is identified in the Microsoft documentation as harmful.",
                Recommendation = "Disable AUTO_SHRINK immediately. Only shrink files manually when there is a genuine permanent reduction in database size, followed by an index rebuild.",
                ServiceWindow = ServiceWindowAdvisor.No("Disabling AUTO_SHRINK takes effect immediately."),
                FixScript = $"ALTER DATABASE [{context.Snapshot.DatabaseName}] SET AUTO_SHRINK OFF;",
                Evidence =
                [
                    new FindingEvidence("AutoShrink", "ON"),
                ],
            },
        ]);
    }
}

internal sealed class AutoCloseCheck : IHealthCheck
{
    public string Id => "DB-002";

    public string Title => "AUTO_CLOSE enabled";

    public string Category => "Configuration";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var opts = context.Snapshot.DatabaseOptions;
        if (opts?.AutoClose != true)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(
        [
            new AuditFinding
            {
                Id = "DB-002-AUTO-CLOSE",
                Title = "AUTO_CLOSE is enabled",
                Category = Category,
                Severity = AuditSeverity.Medium,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = "AUTO_CLOSE causes SQL Server to fully shut down the database and release resources when the last connection closes. This is inappropriate for any server-based workload.",
                Impact = "Every new connection incurs a cold-start penalty: the entire database must be opened, plans must be recompiled, and buffer pool must be re-warmed. This can cause severe latency spikes on reconnect.",
                Recommendation = "Disable AUTO_CLOSE. It is primarily a legacy option intended for desktop/file-based scenarios.",
                ServiceWindow = ServiceWindowAdvisor.No("Disabling AUTO_CLOSE takes effect immediately."),
                FixScript = $"ALTER DATABASE [{context.Snapshot.DatabaseName}] SET AUTO_CLOSE OFF;",
                Evidence =
                [
                    new FindingEvidence("AutoClose", "ON"),
                ],
            },
        ]);
    }
}

internal sealed class PageVerifyCheck : IHealthCheck
{
    public string Id => "DB-003";

    public string Title => "PAGE_VERIFY not set to CHECKSUM";

    public string Category => "Configuration";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var opts = context.Snapshot.DatabaseOptions;
        if (opts is null)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        if (string.Equals(opts.PageVerify, "CHECKSUM", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(
        [
            new AuditFinding
            {
                Id = "DB-003-PAGE-VERIFY",
                Title = $"PAGE_VERIFY is '{opts.PageVerify}' instead of CHECKSUM",
                Category = Category,
                Severity = AuditSeverity.High,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = $"PAGE_VERIFY is set to '{opts.PageVerify}'. Without CHECKSUM, SQL Server cannot detect torn writes or storage corruption when reading data pages. CHECKSUM causes a checksum to be written with every page and verified on every read.",
                Impact = "Silent data corruption can go undetected until a query returns wrong results or a restore fails. Without checksums, DBCC CHECKDB cannot detect all forms of corruption.",
                Recommendation = "Enable PAGE_VERIFY CHECKSUM. This is the default for new databases and should be considered mandatory for any production or important database.",
                ServiceWindow = ServiceWindowAdvisor.No("Changing PAGE_VERIFY takes effect on new page writes. No restart required."),
                FixScript = $"ALTER DATABASE [{context.Snapshot.DatabaseName}] SET PAGE_VERIFY CHECKSUM;",
                Evidence =
                [
                    new FindingEvidence("CurrentPageVerify", opts.PageVerify),
                    new FindingEvidence("Recommended", "CHECKSUM"),
                ],
            },
        ]);
    }
}

internal sealed class RcsiAdvisoryCheck : IHealthCheck
{
    public string Id => "DB-004";

    public string Title => "READ_COMMITTED_SNAPSHOT isolation not enabled";

    public string Category => "Configuration";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var opts = context.Snapshot.DatabaseOptions;
        if (opts?.IsRcsiEnabled != false)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        if (context.Snapshot.ActiveBlockingSessions.Count == 0)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(
        [
            new AuditFinding
            {
                Id = "DB-004-RCSI",
                Title = "READ_COMMITTED_SNAPSHOT isolation is disabled with active blocking sessions",
                Category = Category,
                Severity = AuditSeverity.Info,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = $"RCSI is disabled and the database currently has {context.Snapshot.ActiveBlockingSessions.Count} blocking session(s). RCSI eliminates most reader-writer blocking by providing readers with a consistent snapshot of data without taking shared locks.",
                Impact = "Without RCSI, reads block writes and writes block reads under the default READ COMMITTED isolation level, leading to contention and wait-time.",
                Recommendation = "Enabling RCSI is an architectural decision that requires careful testing. However, it dramatically reduces read-write contention for OLTP workloads. Enabling requires setting the database to single-user mode briefly.",
                ServiceWindow = ServiceWindowAdvisor.No("Enabling RCSI requires a brief single-user mode transition, but the setting itself is low-risk."),
                FixScript = $"""
                    -- Review this advisory carefully before enabling RCSI.
                    -- Enabling RCSI will briefly put the database in SINGLE_USER mode.
                    ALTER DATABASE [{context.Snapshot.DatabaseName}] SET READ_COMMITTED_SNAPSHOT ON;
                    """,
                Evidence =
                [
                    new FindingEvidence("RCSI", "OFF"),
                    new FindingEvidence("ActiveBlockingSessions", context.Snapshot.ActiveBlockingSessions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ],
            },
        ]);
    }
}

internal sealed class QueryStoreDisabledCheck : IHealthCheck
{
    public string Id => "DB-005";

    public string Title => "Query Store is disabled";

    public string Category => "Configuration";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var opts = context.Snapshot.DatabaseOptions;
        if (opts?.QueryStoreEnabled != false)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        if (context.Snapshot.Tables.Count < 10)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(
        [
            new AuditFinding
            {
                Id = "DB-005-QS-DISABLED",
                Title = "Query Store is disabled",
                Category = Category,
                Severity = AuditSeverity.Low,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = "Query Store is not enabled. Query Store is SQL Server's built-in plan history and regression detection tool, available since SQL Server 2016.",
                Impact = "Without Query Store, there is no automated mechanism to detect plan regressions after statistics updates or upgrades, and no history of query performance over time.",
                Recommendation = "Enable Query Store with appropriate data retention and size limits. The default settings are a good starting point for most databases.",
                ServiceWindow = ServiceWindowAdvisor.No("Enabling Query Store is online and non-blocking."),
                FixScript = $"""
                    ALTER DATABASE [{context.Snapshot.DatabaseName}]
                    SET QUERY_STORE = ON (
                        OPERATION_MODE = READ_WRITE,
                        CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = 30),
                        DATA_FLUSH_INTERVAL_SECONDS = 900,
                        MAX_STORAGE_SIZE_MB = 1024,
                        QUERY_CAPTURE_MODE = AUTO
                    );
                    """,
                Evidence =
                [
                    new FindingEvidence("QueryStoreEnabled", "OFF"),
                    new FindingEvidence("TableCount", context.Snapshot.Tables.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ],
            },
        ]);
    }
}

internal sealed class QueryStoreReadOnlyCheck : IHealthCheck
{
    public string Id => "DB-006";

    public string Title => "Query Store is in READ_ONLY mode (storage full)";

    public string Category => "Configuration";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var opts = context.Snapshot.DatabaseOptions;
        if (opts?.QueryStoreEnabled != true)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        if (!string.Equals(opts.QueryStoreState, "READ_ONLY", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(
        [
            new AuditFinding
            {
                Id = "DB-006-QS-READONLY",
                Title = "Query Store is READ_ONLY because its storage is full",
                Category = Category,
                Severity = AuditSeverity.Medium,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = "Query Store has switched to READ_ONLY mode because it has reached its maximum storage size. In this state, Query Store stops recording new query executions and plan changes, silently losing performance history.",
                Impact = "Plan regression detection is disabled. New queries and plan changes after Query Store went full will not be captured. This defeats the purpose of having Query Store enabled.",
                Recommendation = "Increase the Query Store maximum size and/or reduce the stale query threshold. Then force Query Store back to READ_WRITE mode.",
                ServiceWindow = ServiceWindowAdvisor.No("Resizing Query Store and switching to READ_WRITE is online."),
                FixScript = $"""
                    -- Increase Query Store max size and re-enable READ_WRITE:
                    ALTER DATABASE [{context.Snapshot.DatabaseName}]
                    SET QUERY_STORE (
                        MAX_STORAGE_SIZE_MB = 2048,
                        CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = 14),
                        OPERATION_MODE = READ_WRITE
                    );
                    """,
                Evidence =
                [
                    new FindingEvidence("QueryStoreState", opts.QueryStoreState),
                ],
            },
        ]);
    }
}
