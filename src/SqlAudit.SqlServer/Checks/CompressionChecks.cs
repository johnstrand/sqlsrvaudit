using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;
using System.Globalization;

namespace SqlAudit.SqlServer.Checks;

internal sealed class UncompressedLargeTableCheck : IHealthCheck
{
    public string Id => "COMP-001";

    public string Title => "Uncompressed large tables";

    public string Category => "Compression";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();

        foreach (var table in context.Snapshot.TableCompression
            .Where(t => t.DataCompression.Equals("NONE", StringComparison.OrdinalIgnoreCase)
                        && t.Rows >= context.Options.LargeTableRowThreshold
                        && t.UsedPageCount >= 1000))
        {
            var tableName = SqlName.Table(table.SchemaName, table.TableName);
            var sizeMb = table.UsedPageCount * 8m / 1024m;

            findings.Add(new AuditFinding
            {
                Id = $"COMP-001-{table.ObjectId}-{table.PartitionNumber}",
                Title = "Large uncompressed table",
                Category = Category,
                Severity = AuditSeverity.Info,
                DatabaseObject = tableName,
                Description = $"Table {tableName} has {table.Rows:N0} rows and uses {sizeMb:F0} MB of space with no compression. Row compression can reduce space usage and improve I/O performance at a small CPU cost.",
                Impact = "Uncompressed tables consume more storage and generate more I/O, which can slow down full scans and reduce buffer pool efficiency.",
                Recommendation = "Test ROW compression first — it is generally safe and has minimal impact. Evaluate PAGE compression for even greater reduction (test for CPU impact first).",
                ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                    AuditOperationRisk.PotentiallyOnlineIndexBuild,
                    "REBUILD with ONLINE = ON does not require downtime but does consume I/O and CPU."),
                FixScript =$"""
                    -- Test row compression first with an online rebuild.
                    -- RequiresServiceWindow: false (WITH ONLINE = ON)
                    ALTER TABLE {tableName}
                    REBUILD PARTITION = ALL WITH (DATA_COMPRESSION = ROW, ONLINE = ON);
                    """,
                Evidence =
                [
                    new FindingEvidence("Rows", table.Rows.ToString("N0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("UsedMb", sizeMb.ToString("F0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("Compression", table.DataCompression),
                    new FindingEvidence("Partition", table.PartitionNumber.ToString(CultureInfo.InvariantCulture)),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}
