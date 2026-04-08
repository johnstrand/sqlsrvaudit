using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;
using System.Globalization;

namespace SqlAudit.SqlServer.Checks;

internal sealed class FullBackupRecencyCheck : IHealthCheck
{
    public string Id => "BAK-001";

    public string Title => "Full backup recency";

    public string Category => "Backup";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var bp = context.Snapshot.BackupPosture;
        if (bp is null)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        AuditSeverity severity;
        string description;
        string impact;

        if (bp.LastFullBackupUtc is null)
        {
            severity = AuditSeverity.Critical;
            description = "No full backup has ever been recorded for this database. This means there is no recovery baseline.";
            impact = "Complete data loss in case of failure — no restore point exists.";
        }
        else if (bp.FullBackupAgeHours > 720)
        {
            severity = AuditSeverity.High;
            description = $"The most recent full backup is {bp.FullBackupAgeHours!.Value.ToString("F0", CultureInfo.InvariantCulture)} hours old ({bp.FullBackupAgeHours.Value / 24:F1} days). Backups older than 30 days represent a significant RPO risk.";
            impact = "Restoration to a recent point in time will be slow or impossible.";
        }
        else if (bp.FullBackupAgeHours > 168)
        {
            severity = AuditSeverity.Medium;
            description = $"The most recent full backup is {bp.FullBackupAgeHours!.Value.ToString("F0", CultureInfo.InvariantCulture)} hours old ({bp.FullBackupAgeHours.Value / 24:F1} days). Best practice is to take full backups at least weekly.";
            impact = "Differential and log backups since the last full are the only restore chain. Any backup chain break means starting from an old full.";
        }
        else
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(
        [
            new AuditFinding
            {
                Id = "BAK-001-FULL",
                Title = bp.LastFullBackupUtc is null ? "No full backup on record" : "Full backup is overdue",
                Category = Category,
                Severity = severity,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = description,
                Impact = impact,
                Recommendation = "Take an immediate full backup and establish a regular backup schedule. For critical databases, take full backups daily or at minimum weekly.",
                ServiceWindow = ServiceWindowAdvisor.No("Taking a backup is non-blocking and does not require a service window."),
                Evidence =
                [
                    new FindingEvidence("LastFullBackupUtc", bp.LastFullBackupUtc?.ToString("u") ?? "Never"),
                    new FindingEvidence("FullBackupAgeHours", bp.FullBackupAgeHours?.ToString("F1", CultureInfo.InvariantCulture) ?? "N/A"),
                    new FindingEvidence("RecoveryModel", bp.RecoveryModel),
                ],
            },
        ]);
    }
}

internal sealed class LogBackupForFullRecoveryCheck : IHealthCheck
{
    public string Id => "BAK-002";

    public string Title => "Log backups for FULL recovery model database";

    public string Category => "Backup";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var bp = context.Snapshot.BackupPosture;
        if (bp is null)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var isFullOrBulk = string.Equals(bp.RecoveryModel, "FULL", StringComparison.OrdinalIgnoreCase)
            || string.Equals(bp.RecoveryModel, "BULK_LOGGED", StringComparison.OrdinalIgnoreCase);

        if (!isFullOrBulk)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        AuditSeverity severity;
        string description;

        if (bp.LastLogBackupUtc is null)
        {
            severity = AuditSeverity.Critical;
            description = $"Database is in {bp.RecoveryModel} recovery model but no transaction log backups have ever been taken. The transaction log will grow indefinitely and cannot be truncated.";
        }
        else if (bp.LogBackupAgeHours > 60)
        {
            severity = AuditSeverity.High;
            description = $"Database is in {bp.RecoveryModel} recovery model. The most recent log backup is {bp.LogBackupAgeHours!.Value.ToString("F0", CultureInfo.InvariantCulture)} hours old. Log backups should be taken at regular intervals (e.g., every 15–60 minutes for production).";
        }
        else
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(
        [
            new AuditFinding
            {
                Id = "BAK-002-LOG",
                Title = bp.LastLogBackupUtc is null
                    ? $"No log backups taken for {bp.RecoveryModel} recovery database"
                    : $"Log backups are overdue for {bp.RecoveryModel} recovery database",
                Category = Category,
                Severity = severity,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = description,
                Impact = bp.LastLogBackupUtc is null
                    ? "The transaction log will grow unbounded, potentially filling disk. Point-in-time recovery is not possible."
                    : "Extended data loss window. Recovery to a recent point in time will not be possible for data committed since the last log backup.",
                Recommendation = "Configure automated log backups using SQL Server Agent or a backup solution. For production OLTP databases, log backups every 15–60 minutes is typical.",
                ServiceWindow = ServiceWindowAdvisor.No("Taking a log backup is non-blocking and does not require a service window."),
                Evidence =
                [
                    new FindingEvidence("RecoveryModel", bp.RecoveryModel),
                    new FindingEvidence("LastLogBackupUtc", bp.LastLogBackupUtc?.ToString("u") ?? "Never"),
                    new FindingEvidence("LogBackupAgeHours", bp.LogBackupAgeHours?.ToString("F1", CultureInfo.InvariantCulture) ?? "N/A"),
                ],
            },
        ]);
    }
}

internal sealed class DifferentialBackupGapCheck : IHealthCheck
{
    public string Id => "BAK-003";

    public string Title => "No recent differential backup";

    public string Category => "Backup";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var bp = context.Snapshot.BackupPosture;
        if (bp is null)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        if (!(bp.FullBackupAgeHours > 24))
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        if (!(bp.DifferentialBackupAgeHours is null || bp.DifferentialBackupAgeHours > 72))
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var diffAge = bp.DifferentialBackupAgeHours is null
            ? "never"
            : $"{bp.DifferentialBackupAgeHours.Value.ToString("F0", CultureInfo.InvariantCulture)} hours ago";

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(
        [
            new AuditFinding
            {
                Id = "BAK-003-DIFF",
                Title = "No recent differential backup",
                Category = Category,
                Severity = AuditSeverity.Low,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = $"The last full backup was {bp.FullBackupAgeHours!.Value.ToString("F0", CultureInfo.InvariantCulture)} hours ago. The last differential backup was taken {diffAge}. Without recent differentials, restore operations require replaying all log backups since the last full backup.",
                Impact = "Longer restore time in disaster recovery scenarios. Full backup + all log backups must be replayed, increasing RTO.",
                Recommendation = "Consider adding daily differential backups between weekly full backups to speed up restore operations.",
                ServiceWindow = ServiceWindowAdvisor.No("Taking a differential backup is non-blocking and does not require a service window."),
                Evidence =
                [
                    new FindingEvidence("FullBackupAgeHours", bp.FullBackupAgeHours.Value.ToString("F0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("LastDifferentialBackupUtc", bp.LastDifferentialBackupUtc?.ToString("u") ?? "Never"),
                    new FindingEvidence("DifferentialBackupAgeHours", bp.DifferentialBackupAgeHours?.ToString("F1", CultureInfo.InvariantCulture) ?? "N/A"),
                ],
            },
        ]);
    }
}
