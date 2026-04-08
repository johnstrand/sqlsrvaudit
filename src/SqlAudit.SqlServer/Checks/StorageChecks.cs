using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;
using System.Globalization;

namespace SqlAudit.SqlServer.Checks;

internal sealed class LowDiskSpaceCheck : IHealthCheck
{
    private const decimal WarningThresholdPercent = 15m;
    private const decimal HighThresholdPercent = 5m;

    public string Id => "STOR-001";

    public string Title => "Low disk space on database volume";

    public string Category => "Storage";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var volumeStats = context.Snapshot.VolumeStats;
        if (volumeStats.Count == 0)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var findings = new List<AuditFinding>();

        foreach (var byVolume in volumeStats.GroupBy(v => v.VolumeMount, StringComparer.OrdinalIgnoreCase))
        {
            var first = byVolume.First();
            if (first.AvailablePercent >= WarningThresholdPercent)
            {
                continue;
            }

            var severity = first.AvailablePercent < HighThresholdPercent ? AuditSeverity.High : AuditSeverity.Medium;
            var totalGb = first.TotalBytes / (1024m * 1024 * 1024);
            var availGb = first.AvailableBytes / (1024m * 1024 * 1024);

            findings.Add(new AuditFinding
            {
                Id = $"STOR-001-{byVolume.Key.Replace('\\', '-').Replace('/', '-').TrimStart('-')}",
                Title = $"Low disk space on volume {byVolume.Key}",
                Category = Category,
                Severity = severity,
                DatabaseObject = byVolume.Key,
                Description = $"Volume '{byVolume.Key}' has {availGb.ToString("F1", CultureInfo.InvariantCulture)} GB free of {totalGb.ToString("F1", CultureInfo.InvariantCulture)} GB total ({first.AvailablePercent.ToString("F1", CultureInfo.InvariantCulture)}% available). Database files on this volume: {string.Join(", ", byVolume.Select(v => v.LogicalName))}.",
                Impact = "If the volume fills completely, SQL Server cannot grow database files, causing transaction failures and potential database unavailability.",
                Recommendation = "Free space by archiving/purging data, moving files to other volumes, or expanding the volume. Consider adding a disk space alert.",
                ServiceWindow = ServiceWindowAdvisor.No("This is an observational finding. Disk cleanup or file moves may require a maintenance window depending on scope."),
                Evidence =
                [
                    new FindingEvidence("VolumeMount", byVolume.Key),
                    new FindingEvidence("AvailableGb", availGb.ToString("F1", CultureInfo.InvariantCulture)),
                    new FindingEvidence("TotalGb", totalGb.ToString("F1", CultureInfo.InvariantCulture)),
                    new FindingEvidence("AvailablePercent", first.AvailablePercent.ToString("F1", CultureInfo.InvariantCulture) + "%"),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class DataAndLogOnSameVolumeCheck : IHealthCheck
{
    public string Id => "STOR-002";

    public string Title => "Data and log files on the same volume";

    public string Category => "Storage";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var volumeStats = context.Snapshot.VolumeStats;
        if (volumeStats.Count == 0)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var dataVolumes = volumeStats
            .Where(v => string.Equals(v.FileType, "ROWS", StringComparison.OrdinalIgnoreCase))
            .Select(v => v.VolumeMount)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var logFilesOnDataVolumes = volumeStats
            .Where(v => string.Equals(v.FileType, "LOG", StringComparison.OrdinalIgnoreCase)
                        && dataVolumes.Contains(v.VolumeMount))
            .ToArray();

        if (logFilesOnDataVolumes.Length == 0)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var logNames = string.Join(", ", logFilesOnDataVolumes.Select(v => v.LogicalName));
        var volumes = string.Join(", ", logFilesOnDataVolumes.Select(v => v.VolumeMount).Distinct(StringComparer.OrdinalIgnoreCase));

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(
        [
            new AuditFinding
            {
                Id = "STOR-002-MIXED-VOLUME",
                Title = "Data and log files share the same storage volume",
                Category = Category,
                Severity = AuditSeverity.Low,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = $"Log file(s) [{logNames}] reside on the same volume(s) as data files ({volumes}). SQL Server log writes are sequential and benefit from dedicated I/O paths.",
                Impact = "Log writes compete with data file I/O for the same disk bandwidth and queue depth. Under write-heavy workloads this can increase log write latency and transaction throughput.",
                Recommendation = "Move log files to a dedicated volume separate from data files. This is especially important for high-transaction-rate databases.",
                ServiceWindow = ServiceWindowAdvisor.No("Moving database files requires detach/attach or ALTER DATABASE … MODIFY FILE, which needs careful planning."),
                Evidence =
                [
                    new FindingEvidence("AffectedLogFiles", logNames),
                    new FindingEvidence("SharedVolumes", volumes),
                ],
            },
        ]);
    }
}
