using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;
using System.Globalization;

namespace SqlAudit.SqlServer.Checks;

internal sealed class DataFileReadLatencyCheck : IHealthCheck
{
    public string Id => "IO-001";

    public string Title => "Data file read latency";

    public string Category => "I/O";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();

        foreach (var file in context.Snapshot.FileIoLatency.Where(f => string.Equals(f.FileType, "ROWS", StringComparison.Ordinal) && f.ReadIoCount > 100))
        {
            AuditSeverity? severity = file.AvgReadLatencyMs switch
            {
                > 50 => AuditSeverity.High,
                > 20 => AuditSeverity.Medium,
                _ => null,
            };

            if (severity is null)
            {
                continue;
            }

            findings.Add(new AuditFinding
            {
                Id = $"IO-001-{file.DatabaseId}-{file.FileId}",
                Title = "High data file read latency",
                Category = Category,
                Severity = severity.Value,
                DatabaseObject = $"db:{file.DatabaseId} / {file.LogicalName}",
                Description = $"Data file '{file.LogicalName}' (database ID {file.DatabaseId}) has an average read latency of {file.AvgReadLatencyMs:F1} ms over {file.ReadIoCount:N0} reads since last restart.",
                Impact = "Slow data file reads directly increase query response times and wait time for PAGEIOLATCH waits.",
                Recommendation = "Investigate storage subsystem performance. Consider moving data files to faster storage (SSD/NVMe). Review I/O patterns for optimization opportunities.",
                ServiceWindow = ServiceWindowAdvisor.No("Observational finding — no schema change required."),
                Evidence =
                [
                    new FindingEvidence("LogicalName", file.LogicalName),
                    new FindingEvidence("AvgReadLatencyMs", file.AvgReadLatencyMs.ToString("F1", CultureInfo.InvariantCulture)),
                    new FindingEvidence("ReadIoCount", file.ReadIoCount.ToString("N0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("SizeMb", file.SizeMb.ToString("F0", CultureInfo.InvariantCulture)),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class LogFileWriteLatencyCheck : IHealthCheck
{
    public string Id => "IO-002";

    public string Title => "Log file write latency";

    public string Category => "I/O";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();

        foreach (var file in context.Snapshot.FileIoLatency.Where(f => string.Equals(f.FileType, "LOG", StringComparison.Ordinal) && f.WriteIoCount > 100))
        {
            AuditSeverity? severity = file.AvgWriteLatencyMs switch
            {
                > 20 => AuditSeverity.High,
                > 5 => AuditSeverity.Medium,
                _ => null,
            };

            if (severity is null)
            {
                continue;
            }

            findings.Add(new AuditFinding
            {
                Id = $"IO-002-{file.DatabaseId}-{file.FileId}",
                Title = "High log file write latency",
                Category = Category,
                Severity = severity.Value,
                DatabaseObject = $"db:{file.DatabaseId} / {file.LogicalName}",
                Description = $"Log file '{file.LogicalName}' (database ID {file.DatabaseId}) has an average write latency of {file.AvgWriteLatencyMs:F1} ms over {file.WriteIoCount:N0} writes since last restart. Transaction log writes are synchronous; latency here directly impacts commit times.",
                Impact = "High log write latency directly increases transaction commit times for all write workloads on this database.",
                Recommendation = "Move transaction log files to dedicated storage with low write latency (SSD/NVMe). Ensure no other workload is competing for the same storage.",
                ServiceWindow = ServiceWindowAdvisor.No("Observational finding — no schema change required."),
                Evidence =
                [
                    new FindingEvidence("LogicalName", file.LogicalName),
                    new FindingEvidence("AvgWriteLatencyMs", file.AvgWriteLatencyMs.ToString("F1", CultureInfo.InvariantCulture)),
                    new FindingEvidence("WriteIoCount", file.WriteIoCount.ToString("N0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("SizeMb", file.SizeMb.ToString("F0", CultureInfo.InvariantCulture)),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}
