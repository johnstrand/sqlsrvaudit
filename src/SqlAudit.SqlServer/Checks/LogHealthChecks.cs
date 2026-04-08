using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;
using System.Globalization;

namespace SqlAudit.SqlServer.Checks;

internal sealed class HighVlfCountCheck : IHealthCheck
{
    private const int WarningThreshold = 200;
    private const int HighThreshold = 1000;

    public string Id => "LOG-001";

    public string Title => "High VLF count in transaction log";

    public string Category => "Log Health";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var log = context.Snapshot.LogHealth;
        if (log is null || log.VlfCount <= WarningThreshold)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var severity = log.VlfCount > HighThreshold ? AuditSeverity.High : AuditSeverity.Medium;

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(
        [
            new AuditFinding
            {
                Id = "LOG-001-HIGH-VLF",
                Title = "Transaction log has excessive Virtual Log Files (VLFs)",
                Category = Category,
                Severity = severity,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = $"The transaction log has {log.VlfCount.ToString(CultureInfo.InvariantCulture)} VLFs. High VLF counts (>200) indicate that the log file grew in many small increments, fragmenting the log internally.",
                Impact = "Excessive VLFs slow database recovery, log backup restore, and database mirroring/availability group log-send operations.",
                Recommendation = "Shrink the log file to near-zero and re-expand it in one large operation matching your expected log usage. This consolidates VLFs. Increase log auto-growth to a fixed size (not percent).",
                ServiceWindow = ServiceWindowAdvisor.No("Log shrink and re-expand can be done online, but should be performed during low-activity periods."),
                FixScript = $"""
                    -- Shrink and re-expand the log to consolidate VLFs.
                    -- TODO: Replace <log_logical_name> and <target_size_mb> with the actual values.
                    -- Step 1: Ensure no active transactions are using the log.
                    -- Step 2: Shrink the log:
                    DBCC SHRINKFILE (<log_logical_name>, 1);
                    -- Step 3: Expand to a single large VLF (use a size that is a multiple of 512 MB for optimal VLF count):
                    ALTER DATABASE [{context.Snapshot.DatabaseName}] MODIFY FILE (NAME = N'<log_logical_name>', SIZE = <target_size_mb>MB);
                    """,
                Evidence =
                [
                    new FindingEvidence("VlfCount", log.VlfCount.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("LogSizeMb", log.TotalLogSizeMb.ToString("F1", CultureInfo.InvariantCulture)),
                    new FindingEvidence("UsedLogPercent", log.UsedLogPercent.ToString("F1", CultureInfo.InvariantCulture) + "%"),
                ],
            },
        ]);
    }
}

internal sealed class LogReuseWaitCheck : IHealthCheck
{
    public string Id => "LOG-002";

    public string Title => "Log reuse blocked";

    public string Category => "Log Health";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var log = context.Snapshot.LogHealth;
        if (log is null)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var wait = log.LogReuseWaitDescription;
        if (string.IsNullOrEmpty(wait)
            || string.Equals(wait, "NOTHING", StringComparison.OrdinalIgnoreCase)
            || string.Equals(wait, "LOG_BACKUP", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var (severity, description, recommendation) = GetWaitGuidance(wait);

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(
        [
            new AuditFinding
            {
                Id = $"LOG-002-{wait.Replace(' ', '-').ToUpperInvariant()}",
                Title = $"Transaction log reuse is blocked: {wait}",
                Category = Category,
                Severity = severity,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = description,
                Impact = "When log reuse is blocked, the transaction log cannot reclaim space, causing it to grow unbounded until the blocking condition is resolved.",
                Recommendation = recommendation,
                ServiceWindow = ServiceWindowAdvisor.No("Resolving log reuse wait is an operational action, not a schema change."),
                Evidence =
                [
                    new FindingEvidence("LogReuseWait", wait),
                    new FindingEvidence("LogSizeMb", log.TotalLogSizeMb.ToString("F1", CultureInfo.InvariantCulture)),
                    new FindingEvidence("UsedLogPercent", log.UsedLogPercent.ToString("F1", CultureInfo.InvariantCulture) + "%"),
                ],
            },
        ]);
    }

    private static (AuditSeverity Severity, string Description, string Recommendation) GetWaitGuidance(string wait)
    {
        return wait.ToUpperInvariant() switch
        {
            "ACTIVE_TRANSACTION" => (
                AuditSeverity.High,
                "The transaction log cannot be truncated because a long-running or sleeping transaction has an active open transaction that prevents log truncation.",
                "Identify the oldest active transaction with DBCC OPENTRAN(). Kill idle sessions with open transactions. Review application connection handling."),
            "CHECKPOINT" => (
                AuditSeverity.Medium,
                "Log reuse is waiting for a CHECKPOINT. The checkpoint process is not running frequently enough to reclaim log space.",
                "This often resolves on its own. If persistent, check for I/O bottlenecks that slow checkpoint. Consider increasing the 'recovery interval (min)' server configuration."),
            "DATABASE_MIRRORING" or "AVAILABILITY_REPLICA" => (
                AuditSeverity.High,
                $"Log reuse is blocked by {wait}. The secondary replica or mirror is lagging in applying log records, preventing the primary from truncating the log.",
                "Check the synchronisation health of the secondary replica or mirror partner. Look for network latency, I/O bottlenecks on the secondary, or a disconnected partner."),
            "REPLICATION" => (
                AuditSeverity.High,
                "Log reuse is blocked by replication. The log reader agent has not delivered all transactions to the distributor, preventing log truncation.",
                "Check replication monitor for the Log Reader Agent latency. Look for distributor backlog, network issues, or a stopped Log Reader Agent job."),
            "DATABASE_SNAPSHOT_CREATION" => (
                AuditSeverity.Low,
                "A database snapshot creation is temporarily preventing log reuse. This is usually transient.",
                "This will resolve once the snapshot creation completes. If persistent, check for a snapshot creation that is stuck or blocked."),
            "ACTIVE_BACKUP_OR_RESTORE" => (
                AuditSeverity.Low,
                "An active backup or restore operation is preventing log truncation. This is expected during backup operations.",
                "This will resolve once the backup or restore completes. Verify the backup is progressing normally."),
            _ => (
                AuditSeverity.Medium,
                $"The log reuse wait description is '{wait}'. The transaction log cannot truncate its inactive portion while this condition persists.",
                $"Investigate the root cause of '{wait}'. Consult the SQL Server documentation for this specific wait type and resolve the blocking condition."),
        };
    }
}
