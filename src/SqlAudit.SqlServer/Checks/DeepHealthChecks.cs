using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;
using System.Globalization;

namespace SqlAudit.SqlServer.Checks;

internal sealed class MissingPrimaryKeyCheck : IHealthCheck
{
    public string Id => "PK-001";

    public string Title => "Missing primary keys";

    public string Category => "Keys";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();

        foreach (var table in context.Snapshot.Tables.Where(t => !t.HasPrimaryKey))
        {
            var severity = table.RowCount >= context.Options.LargeTableRowThreshold
                ? AuditSeverity.High
                : AuditSeverity.Medium;
            var tableName = SqlName.Table(table.SchemaName, table.TableName);
            var constraintName = SqlName.Constraint($"PK_{SqlName.ObjectNameSuffix(table.TableName)}");

            findings.Add(new AuditFinding
            {
                Id = $"PK-001-{table.ObjectId}",
                Title = "Table has no primary key",
                Category = Category,
                Severity = severity,
                DatabaseObject = tableName,
                Description = "The table does not define a primary key.",
                Impact = "Data integrity guarantees and optimizer assumptions are weaker.",
                Recommendation = "Define a stable, narrow primary key.",
                ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                    AuditOperationRisk.ConstraintValidation,
                    "Adding a primary key validates existing rows and can lock the table."),
                FixScript = $"""
                    -- RequiresServiceWindow: true
                    -- Reason: Adding a primary key can lock and scan existing rows.
                    -- TODO: Replace <key_column> with the real key column(s).
                    ALTER TABLE {tableName}
                    ADD CONSTRAINT {constraintName} PRIMARY KEY CLUSTERED (<key_column>);
                    """,
                Evidence =
                [
                    new FindingEvidence("Rows", table.RowCount.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("ReservedMB", table.ReservedMb.ToString(CultureInfo.InvariantCulture)),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class LargeHeapTableCheck : IHealthCheck
{
    public string Id => "HEAP-001";

    public string Title => "Large heap tables";

    public string Category => "Indexes";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();

        foreach (var table in context.Snapshot.Tables.Where(t => t.IsHeap && t.RowCount >= context.Options.LargeTableRowThreshold))
        {
            var tableName = SqlName.Table(table.SchemaName, table.TableName);
            var indexName = SqlName.Index($"CX_{SqlName.ObjectNameSuffix(table.TableName)}");

            findings.Add(new AuditFinding
            {
                Id = $"HEAP-001-{table.ObjectId}",
                Title = "Large table is stored as heap",
                Category = Category,
                Severity = AuditSeverity.High,
                DatabaseObject = tableName,
                Description = "A high-row-count table has no clustered index.",
                Impact = "Heaps on large tables can increase read amplification and fragmentation.",
                Recommendation = "Create a clustered index on a stable key.",
                ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                    AuditOperationRisk.OfflineIndexBuild,
                    "Building a clustered index on a heap rewrites the table and can block workloads."),
                FixScript = $"""
                    -- RequiresServiceWindow: true
                    -- Reason: Creating clustered index rewrites heap data pages.
                    -- TODO: Replace <cluster_key_column> with the chosen clustering key.
                    CREATE CLUSTERED INDEX {indexName}
                    ON {tableName} (<cluster_key_column>);
                    """,
                Evidence =
                [
                    new FindingEvidence("Rows", table.RowCount.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("ReservedMB", table.ReservedMb.ToString(CultureInfo.InvariantCulture)),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class ForeignKeyDisabledOrUntrustedCheck : IHealthCheck
{
    public string Id => "FK-001";

    public string Title => "Disabled or untrusted foreign keys";

    public string Category => "Constraints";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();

        foreach (var fk in context.Snapshot.ForeignKeys.Where(f => f.IsDisabled || f.IsNotTrusted))
        {
            var parentTable = SqlName.Table(fk.ParentSchema, fk.ParentTable);
            var constraintName = SqlName.Constraint(fk.ForeignKeyName);

            findings.Add(new AuditFinding
            {
                Id = $"FK-001-{fk.ObjectId}",
                Title = "Foreign key is disabled or not trusted",
                Category = Category,
                Severity = AuditSeverity.High,
                DatabaseObject = $"{parentTable}.{constraintName}",
                Description = "A foreign key is disabled and/or not trusted by the optimizer.",
                Impact = "Integrity checks may be bypassed and join/cardinality plans can degrade.",
                Recommendation = "Re-enable and validate the foreign key with CHECK CHECK.",
                ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                    AuditOperationRisk.ConstraintValidation,
                    "Constraint validation scans existing rows and can introduce blocking."),
                FixScript = $"""
                    -- RequiresServiceWindow: true
                    -- Reason: CHECK CHECK validates all existing rows.
                    ALTER TABLE {parentTable}
                    WITH CHECK CHECK CONSTRAINT {constraintName};
                    """,
                Evidence =
                [
                    new FindingEvidence("IsDisabled", fk.IsDisabled.ToString()),
                    new FindingEvidence("IsNotTrusted", fk.IsNotTrusted.ToString()),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class ForeignKeyWithoutIndexCheck : IHealthCheck
{
    public string Id => "FK-002";

    public string Title => "Foreign keys without supporting index";

    public string Category => "Indexes";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();

        foreach (var fk in context.Snapshot.ForeignKeys.Where(f => !f.HasSupportingIndex))
        {
            var parentTable = SqlName.Table(fk.ParentSchema, fk.ParentTable);
            var idxName = SqlName.Index($"IX_{SqlName.ObjectNameSuffix(fk.ParentTable)}_{SqlName.ObjectNameSuffix(fk.ForeignKeyName)}");

            findings.Add(new AuditFinding
            {
                Id = $"FK-002-{fk.ObjectId}",
                Title = "Foreign key has no matching index",
                Category = Category,
                Severity = AuditSeverity.Medium,
                DatabaseObject = $"{parentTable}.{SqlName.Constraint(fk.ForeignKeyName)}",
                Description = "The foreign key columns are not backed by a matching index prefix.",
                Impact = "Deletes/updates on parent rows and child joins may trigger expensive scans.",
                Recommendation = "Create a nonclustered index on foreign key columns in key order.",
                ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                    AuditOperationRisk.PotentiallyOnlineIndexBuild,
                    "Index creation may still block depending on edition, table shape, and options."),
                FixScript = $"""
                    -- RequiresServiceWindow: true
                    -- Reason: Index creation can lock schema and consume significant resources.
                    CREATE INDEX {idxName}
                    ON {parentTable} ({fk.ParentColumns});
                    """,
                Evidence =
                [
                    new FindingEvidence("FKColumns", fk.ParentColumns),
                    new FindingEvidence("ReferencedTable", SqlName.Table(fk.ReferencedSchema, fk.ReferencedTable)),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class ForeignKeyTypeMismatchCheck : IHealthCheck
{
    public string Id => "FK-003";

    public string Title => "Foreign key column type mismatch";

    public string Category => "Constraints";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = context.Snapshot.ForeignKeys
            .Where(f => !string.Equals(f.ParentColumnTypes, f.ReferencedColumnTypes, StringComparison.OrdinalIgnoreCase))
            .Select(fk =>
            {
                var parentTable = SqlName.Table(fk.ParentSchema, fk.ParentTable);
                return new AuditFinding
                {
                    Id = $"FK-003-{fk.ObjectId}",
                    Title = "Foreign key and referenced key types differ",
                    Category = Category,
                    Severity = AuditSeverity.High,
                    DatabaseObject = $"{parentTable}.{SqlName.Constraint(fk.ForeignKeyName)}",
                    Description = "The parent and referenced column type signatures are not identical.",
                    Impact = "Implicit conversions can reduce seekability and increase CPU usage.",
                    Recommendation = "Align data types, lengths, precision, and scale for both sides.",
                    ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                        AuditOperationRisk.OfflineIndexBuild,
                        "Type changes are schema-affecting and often require data movement and locking."),
                    FixScript = $"""
                        -- RequiresServiceWindow: true
                        -- Reason: Column type alignment typically requires schema migration.
                        -- Manual fix required: align parent and referenced data types.
                        -- Parent types: {fk.ParentColumnTypes}
                        -- Referenced types: {fk.ReferencedColumnTypes}
                        """,
                    Evidence =
                    [
                        new FindingEvidence("ParentTypes", fk.ParentColumnTypes),
                        new FindingEvidence("ReferencedTypes", fk.ReferencedColumnTypes),
                    ],
                };
            })
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class DuplicateIndexCheck : IHealthCheck
{
    public string Id => "IDX-001";

    public string Title => "Duplicate indexes";

    public string Category => "Indexes";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var candidates = context.Snapshot.Indexes
            .Where(i => !i.IsPrimaryKey && !i.IsUniqueConstraint && !i.IsHypothetical)
            .GroupBy(i => new
            {
                i.ObjectId,
                i.IsUnique,
                i.IndexType,
                i.KeyColumns,
                i.IncludedColumns,
                Filter = i.FilterDefinition ?? string.Empty,
            })
            .Where(g => g.Skip(1).Any());

        var findings = new List<AuditFinding>();
        foreach (var group in candidates)
        {
            var ordered = group.OrderBy(i => i.IndexName, StringComparer.Ordinal).ToArray();
            var keep = ordered[0];
            var drop = ordered.Skip(1).ToArray();

            if (drop.Length == 0)
            {
                continue;
            }

            var tableName = SqlName.Table(keep.SchemaName, keep.TableName);
            var drops = string.Join(Environment.NewLine, drop.Select(i => $"DROP INDEX {SqlName.Index(i.IndexName)} ON {tableName};"));

            findings.Add(new AuditFinding
            {
                Id = $"IDX-001-{keep.ObjectId}-{keep.IndexId}",
                Title = "Duplicate index definitions detected",
                Category = Category,
                Severity = AuditSeverity.Medium,
                DatabaseObject = tableName,
                Description = "Multiple indexes share identical key/include/filter definitions.",
                Impact = "Duplicate indexes increase write costs, maintenance time, and storage footprint.",
                Recommendation = "Keep one index and drop redundant copies after workload validation.",
                ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                    AuditOperationRisk.Unknown,
                    "Dropping indexes can still impact concurrent queries and should be planned."),
                FixScript = $"""
                    -- RequiresServiceWindow: true
                    -- Reason: Index drops can block concurrent metadata operations.
                    -- Keep: {SqlName.Index(keep.IndexName)}
                    {drops}
                    """,
                Evidence =
                [
                    new FindingEvidence("Keep", keep.IndexName),
                    new FindingEvidence("Drop", string.Join(", ", drop.Select(i => i.IndexName))),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class OverlappingIndexCheck : IHealthCheck
{
    public string Id => "IDX-002";

    public string Title => "Overlapping index coverage";

    public string Category => "Indexes";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();

        foreach (var indexes in GetComparableIndexGroups(context.Snapshot.Indexes))
        {
            foreach (var narrow in indexes)
            {
                var finding = FindOverlappingIndex(indexes, narrow);
                if (finding is not null)
                {
                    findings.Add(finding);
                }
            }
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }

    private static IEnumerable<IndexInfo[]> GetComparableIndexGroups(IEnumerable<IndexInfo> indexes)
    {
        return indexes
            .Where(i => !i.IsPrimaryKey && !i.IsUniqueConstraint && !i.IsUnique && !i.IsDisabled && !i.IsHypothetical)
            .GroupBy(i => i.ObjectId)
            .Select(g => g.ToArray());
    }

    private static AuditFinding? FindOverlappingIndex(IReadOnlyCollection<IndexInfo> indexes, IndexInfo narrow)
    {
        foreach (var wide in indexes)
        {
            if (narrow.ObjectId == wide.ObjectId && narrow.IndexId == wide.IndexId)
            {
                continue;
            }

            var finding = TryCreateOverlappingIndexFinding(narrow, wide);
            if (finding is not null)
            {
                return finding;
            }
        }

        return null;
    }

    private static AuditFinding? TryCreateOverlappingIndexFinding(IndexInfo narrow, IndexInfo wide)
    {
        if (!IsPrefix(narrow.KeyColumns, wide.KeyColumns)
            || !IncludesSubset(narrow.IncludedColumns, wide.IncludedColumns)
            || !FiltersCompatible(narrow, wide))
        {
            return null;
        }

        var tableName = SqlName.Table(narrow.SchemaName, narrow.TableName);
        return new AuditFinding
        {
            Id = $"IDX-002-{narrow.ObjectId}-{narrow.IndexId}-{wide.IndexId}",
            Title = "Index appears redundant to broader index",
            Category = "Indexes",
            Severity = AuditSeverity.Low,
            DatabaseObject = tableName,
            Description = $"Index {narrow.IndexName} is likely covered by {wide.IndexName}.",
            Impact = "Potentially redundant maintenance overhead for similar access paths.",
            Recommendation = "Validate query plans and drop the redundant index if no regressions occur.",
            ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                AuditOperationRisk.Unknown,
                "Dropping indexes is typically quick but should still be planned for safety."),
            FixScript = $"""
                -- RequiresServiceWindow: true
                -- Reason: Index drop should be scheduled after validating plan stability.
                DROP INDEX {SqlName.Index(narrow.IndexName)} ON {tableName};
                """,
            Evidence =
            [
                new FindingEvidence("CandidateDrop", narrow.IndexName),
                new FindingEvidence("CoverageIndex", wide.IndexName),
                new FindingEvidence("KeyColumns", narrow.KeyColumns),
            ],
        };
    }

    private static bool IsPrefix(string prefix, string target)
    {
        if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        if (string.Equals(prefix, target, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return target.StartsWith(prefix + ",", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IncludesSubset(string subset, string superset)
    {
        var subsetColumns = SplitColumns(subset);
        if (subsetColumns.Count == 0)
        {
            return true;
        }

        var supersetColumns = SplitColumns(superset);
        return subsetColumns.All(supersetColumns.Contains);
    }

    private static bool FiltersCompatible(IndexInfo narrow, IndexInfo wide)
    {
        if (narrow.HasFilter != wide.HasFilter)
        {
            return false;
        }

        if (!narrow.HasFilter)
        {
            return true;
        }

        return string.Equals(narrow.FilterDefinition, wide.FilterDefinition, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> SplitColumns(string list) =>
        list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

internal sealed class DisabledIndexCheck : IHealthCheck
{
    public string Id => "IDX-003";

    public string Title => "Disabled indexes";

    public string Category => "Indexes";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = context.Snapshot.Indexes
            .Where(i => i.IsDisabled)
            .Select(index =>
            {
                var tableName = SqlName.Table(index.SchemaName, index.TableName);
                return new AuditFinding
                {
                    Id = $"IDX-003-{index.ObjectId}-{index.IndexId}",
                    Title = "Index is disabled",
                    Category = Category,
                    Severity = AuditSeverity.Medium,
                    DatabaseObject = $"{tableName}.{SqlName.Index(index.IndexName)}",
                    Description = "Index exists but is disabled.",
                    Impact = "Expected access path is unavailable and can cause severe query regressions.",
                    Recommendation = "Rebuild or drop disabled index based on current workload need.",
                    ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                        AuditOperationRisk.OfflineIndexBuild,
                        "Rebuilding disabled indexes can be resource-intensive and blocking."),
                    FixScript = $"""
                        -- RequiresServiceWindow: true
                        -- Reason: Rebuilding disabled index can block DML.
                        ALTER INDEX {SqlName.Index(index.IndexName)} ON {tableName} REBUILD;
                        """,
                    Evidence = [new FindingEvidence("IndexType", index.IndexType)],
                };
            })
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class UnusedIndexCheck : IHealthCheck
{
    public string Id => "IDX-004";

    public string Title => "Unused write-heavy indexes";

    public string Category => "Indexes";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var usageMap = context.Snapshot.IndexUsage.ToDictionary(u => (u.ObjectId, u.IndexId));
        var findings = new List<AuditFinding>();

        foreach (var index in context.Snapshot.Indexes.Where(IsUnusedIndexCandidate))
        {
            if (!TryGetLowReadHighWriteUsage(context, usageMap, index, out var usage, out var reads))
            {
                continue;
            }

            var tableName = SqlName.Table(index.SchemaName, index.TableName);
            findings.Add(new AuditFinding
            {
                Id = $"IDX-004-{index.ObjectId}-{index.IndexId}",
                Title = "Index has low reads but high writes",
                Category = Category,
                Severity = AuditSeverity.Medium,
                DatabaseObject = $"{tableName}.{SqlName.Index(index.IndexName)}",
                Description = "The index shows very low read usage compared with write maintenance.",
                Impact = "Extra write and maintenance overhead without measurable read benefit.",
                Recommendation = "Validate via query store, then drop or consolidate if not needed.",
                ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                    AuditOperationRisk.Unknown,
                    "Dropping indexes should be scheduled to monitor plan changes."),
                FixScript = $"""
                    -- RequiresServiceWindow: true
                    -- Reason: Index drop can alter plans and should be monitored.
                    DROP INDEX {SqlName.Index(index.IndexName)} ON {tableName};
                    """,
                Evidence =
                [
                    new FindingEvidence("Reads", reads.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("Updates", usage.UserUpdates.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("LastReadUtc", usage.LastReadUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "never"),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }

    private static bool IsUnusedIndexCandidate(IndexInfo index)
    {
        return !index.IsPrimaryKey
               && !index.IsUniqueConstraint
               && !index.IsDisabled
               && !index.IsHypothetical;
    }

    private static bool TryGetLowReadHighWriteUsage(
        HealthCheckContext context,
#pragma warning disable CA1859 // Use concrete types when possible for improved performance
        IReadOnlyDictionary<(int ObjectId, int IndexId), IndexUsageInfo> usageMap,
#pragma warning restore CA1859 // Use concrete types when possible for improved performance
        IndexInfo index,
        out IndexUsageInfo usage,
        out long reads)
    {
        reads = 0;
        if (!usageMap.TryGetValue((index.ObjectId, index.IndexId), out usage!))
        {
            return false;
        }

        reads = usage.UserSeeks + usage.UserScans + usage.UserLookups;
        return reads <= context.Options.UnusedIndexMaxReads
               && usage.UserUpdates >= context.Options.UnusedIndexMinUpdates;
    }
}

internal sealed class FragmentationCheck : IHealthCheck
{
    public string Id => "IDX-005";

    public string Title => "Fragmented indexes";

    public string Category => "Indexes";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var indexLookup = context.Snapshot.Indexes.ToDictionary(i => (i.ObjectId, i.IndexId));
        var findings = new List<AuditFinding>();

        foreach (var stat in context.Snapshot.IndexPhysicalStats)
        {
            if (stat.PageCount < context.Options.FragmentationMinPageCount ||
                stat.FragmentationPercent < context.Options.FragmentationReorganizeThresholdPercent ||
                !indexLookup.TryGetValue((stat.ObjectId, stat.IndexId), out var index) ||
                index.IsDisabled ||
                index.IsHypothetical)
            {
                continue;
            }

            var rebuild = stat.FragmentationPercent >= context.Options.FragmentationRebuildThresholdPercent;
            var tableName = SqlName.Table(index.SchemaName, index.TableName);
            var command = rebuild
                ? $"ALTER INDEX {SqlName.Index(index.IndexName)} ON {tableName} REBUILD WITH (ONLINE = ON);"
                : $"ALTER INDEX {SqlName.Index(index.IndexName)} ON {tableName} REORGANIZE;";

            findings.Add(new AuditFinding
            {
                Id = $"IDX-005-{index.ObjectId}-{index.IndexId}",
                Title = rebuild ? "Index fragmentation exceeds rebuild threshold" : "Index fragmentation exceeds reorganize threshold",
                Category = Category,
                Severity = rebuild ? AuditSeverity.High : AuditSeverity.Medium,
                DatabaseObject = $"{tableName}.{SqlName.Index(index.IndexName)}",
                Description = "Index physical fragmentation is above configured threshold.",
                Impact = "Increased logical reads and longer IO paths for range scans.",
                Recommendation = rebuild
                    ? "Rebuild index during a scheduled window."
                    : "Reorganize index and reassess fill factor patterns.",
                ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                    rebuild ? AuditOperationRisk.PotentiallyOnlineIndexBuild : AuditOperationRisk.IndexReorganize,
                    "Index maintenance can run long and contend with transactional workload."),
                FixScript = $"""
                    -- RequiresServiceWindow: true
                    -- Reason: Index maintenance may impact throughput and blocking behavior.
                    {command}
                    """,
                Evidence =
                [
                    new FindingEvidence("FragmentationPercent", stat.FragmentationPercent.ToString("F2", CultureInfo.InvariantCulture)),
                    new FindingEvidence("PageCount", stat.PageCount.ToString(CultureInfo.InvariantCulture)),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class LowPageDensityCheck : IHealthCheck
{
    public string Id => "IDX-006";

    public string Title => "Low page density indexes";

    public string Category => "Indexes";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var indexLookup = context.Snapshot.Indexes.ToDictionary(i => (i.ObjectId, i.IndexId));
        var findings = new List<AuditFinding>();

        foreach (var stat in context.Snapshot.IndexPhysicalStats)
        {
            if (stat.PageCount < context.Options.FragmentationMinPageCount ||
                stat.AvgPageSpaceUsedPercent >= context.Options.LowPageDensityThresholdPercent ||
                !indexLookup.TryGetValue((stat.ObjectId, stat.IndexId), out var index) ||
                index.IsDisabled ||
                index.IsHypothetical)
            {
                continue;
            }

            var tableName = SqlName.Table(index.SchemaName, index.TableName);
            findings.Add(new AuditFinding
            {
                Id = $"IDX-006-{index.ObjectId}-{index.IndexId}",
                Title = "Index page density is low",
                Category = Category,
                Severity = AuditSeverity.Medium,
                DatabaseObject = $"{tableName}.{SqlName.Index(index.IndexName)}",
                Description = "Average page fullness is below configured density threshold.",
                Impact = "Additional pages increase memory pressure and range scan cost.",
                Recommendation = "Rebuild index and tune fill factor according to write behavior.",
                ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                    AuditOperationRisk.PotentiallyOnlineIndexBuild,
                    "Rebuild can be expensive and may block depending on platform capabilities."),
                FixScript = $"""
                    -- RequiresServiceWindow: true
                    -- Reason: Rebuild may affect concurrency and transaction log usage.
                    ALTER INDEX {SqlName.Index(index.IndexName)} ON {tableName}
                    REBUILD WITH (ONLINE = ON, FILLFACTOR = 90);
                    """,
                Evidence =
                [
                    new FindingEvidence("AvgPageSpaceUsedPercent", stat.AvgPageSpaceUsedPercent.ToString("F2", CultureInfo.InvariantCulture)),
                    new FindingEvidence("PageCount", stat.PageCount.ToString(CultureInfo.InvariantCulture)),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class FillFactorAnomalyCheck : IHealthCheck
{
    public string Id => "IDX-007";

    public string Title => "Potentially over-low fill factor";

    public string Category => "Indexes";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = context.Snapshot.Indexes
            .Where(i => i.FillFactor > 0 && i.FillFactor < 70 && !i.IsDisabled && !i.IsHypothetical)
            .Select(index =>
            {
                var tableName = SqlName.Table(index.SchemaName, index.TableName);
                return new AuditFinding
                {
                    Id = $"IDX-007-{index.ObjectId}-{index.IndexId}",
                    Title = "Index fill factor below 70",
                    Category = Category,
                    Severity = AuditSeverity.Low,
                    DatabaseObject = $"{tableName}.{SqlName.Index(index.IndexName)}",
                    Description = "Configured fill factor is very low.",
                    Impact = "Can materially increase index size and cache footprint.",
                    Recommendation = "Confirm split rates; if unnecessary, increase fill factor.",
                    ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                        AuditOperationRisk.PotentiallyOnlineIndexBuild,
                        "Changing fill factor requires index rebuild."),
                    FixScript = $"""
                        -- RequiresServiceWindow: true
                        -- Reason: Fill factor change requires rebuild.
                        ALTER INDEX {SqlName.Index(index.IndexName)} ON {tableName}
                        REBUILD WITH (ONLINE = ON, FILLFACTOR = 90);
                        """,
                    Evidence = [new FindingEvidence("FillFactor", index.FillFactor.ToString(CultureInfo.InvariantCulture))],
                };
            })
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class StaleStatisticsCheck : IHealthCheck
{
    public string Id => "STAT-001";

    public string Title => "Stale statistics";

    public string Category => "Statistics";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();

        foreach (var stat in context.Snapshot.Statistics)
        {
            if (stat.Rows <= 0)
            {
                continue;
            }

            var thresholdByPercent = (long)Math.Ceiling(stat.Rows * (context.Options.StaleStatsModificationPercent / 100.0));
            var threshold = Math.Max(context.Options.StaleStatsMinModifications, thresholdByPercent);

            if (stat.ModificationCounter < threshold)
            {
                continue;
            }

            var tableName = SqlName.Table(stat.SchemaName, stat.TableName);
            findings.Add(new AuditFinding
            {
                Id = $"STAT-001-{stat.ObjectId}-{stat.StatsId}",
                Title = "Statistics modification count exceeds threshold",
                Category = Category,
                Severity = stat.ModificationCounter > stat.Rows ? AuditSeverity.High : AuditSeverity.Medium,
                DatabaseObject = $"{tableName}.[{stat.StatsName}]",
                Description = "Statistics have changed enough that estimates may be stale.",
                Impact = "Plan quality can degrade due to cardinality misestimation.",
                Recommendation = "Update statistics; use FULLSCAN for critical large objects when needed.",
                ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                    AuditOperationRisk.StatisticsOnly,
                    "Statistics updates are usually online and low-risk compared to DDL changes."),
                FixScript = $"""
                    -- RequiresServiceWindow: false
                    -- Reason: Statistics update is typically online maintenance.
                    UPDATE STATISTICS {tableName} [{stat.StatsName}] WITH FULLSCAN;
                    """,
                Evidence =
                [
                    new FindingEvidence("Rows", stat.Rows.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("ModificationCounter", stat.ModificationCounter.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("Threshold", threshold.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("LastUpdatedUtc", stat.LastUpdatedUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "never"),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class StatisticsConfigurationCheck : IHealthCheck
{
    public string Id => "STAT-002";

    public string Title => "Statistics configuration issues";

    public string Category => "Statistics";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();

        if (!context.Snapshot.AutoCreateStatisticsOn)
        {
            findings.Add(new AuditFinding
            {
                Id = "STAT-002-AUTO-CREATE",
                Title = "AUTO_CREATE_STATISTICS is OFF",
                Category = Category,
                Severity = AuditSeverity.High,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = "The database is not configured to auto-create single-column stats.",
                Impact = "Optimizer may miss useful cardinality information for predicate columns.",
                Recommendation = "Enable AUTO_CREATE_STATISTICS unless workload-specific reasons forbid it.",
                ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                    AuditOperationRisk.MetadataOnly,
                    "Database option change is metadata-level and usually immediate."),
                FixScript = """
                    -- RequiresServiceWindow: false
                    -- Reason: Database option update is metadata-only.
                    ALTER DATABASE CURRENT SET AUTO_CREATE_STATISTICS ON;
                    """,
            });
        }

        if (!context.Snapshot.AutoUpdateStatisticsOn)
        {
            findings.Add(new AuditFinding
            {
                Id = "STAT-002-AUTO-UPDATE",
                Title = "AUTO_UPDATE_STATISTICS is OFF",
                Category = Category,
                Severity = AuditSeverity.High,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = "Automatic statistics updates are disabled.",
                Impact = "Plans can drift as data changes, causing sustained regressions.",
                Recommendation = "Enable AUTO_UPDATE_STATISTICS or schedule frequent updates.",
                ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                    AuditOperationRisk.MetadataOnly,
                    "Database option change is metadata-level and usually immediate."),
                FixScript = """
                    -- RequiresServiceWindow: false
                    -- Reason: Database option update is metadata-only.
                    ALTER DATABASE CURRENT SET AUTO_UPDATE_STATISTICS ON;
                    """,
            });
        }

        findings.AddRange(context.Snapshot.Statistics
            .Where(s => s.IsNoRecompute)
            .Select(stat =>
            {
                var tableName = SqlName.Table(stat.SchemaName, stat.TableName);
                return new AuditFinding
                {
                    Id = $"STAT-002-NORECOMP-{stat.ObjectId}-{stat.StatsId}",
                    Title = "Statistics has NORECOMPUTE enabled",
                    Category = Category,
                    Severity = AuditSeverity.Medium,
                    DatabaseObject = $"{tableName}.[{stat.StatsName}]",
                    Description = "Statistic is configured to skip automatic recomputation.",
                    Impact = "Cardinality estimate drift may accumulate unnoticed.",
                    Recommendation = "Re-evaluate NORECOMPUTE and enable auto recompute when possible.",
                    ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                        AuditOperationRisk.StatisticsOnly,
                        "Updating stat options is low-risk maintenance."),
                    FixScript = $"""
                        -- RequiresServiceWindow: false
                        -- Reason: Statistics option adjustment is low-risk maintenance.
                        UPDATE STATISTICS {tableName} [{stat.StatsName}] WITH RESAMPLE;
                        """,
                };
            }));

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class CompatibilityLevelCheck : IHealthCheck
{
    public string Id => "CFG-001";

    public string Title => "Database compatibility level mismatch";

    public string Category => "Configuration";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        if (context.Snapshot.IsAzureSql
            || !TryResolveExpectedCompatibilityLevel(context.Snapshot.ProductVersion, out var expectedLevel)
            || context.Snapshot.CompatibilityLevel == expectedLevel)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var finding = new AuditFinding
        {
            Id = "CFG-001-COMPATIBILITY-LEVEL",
            Title = "Database compatibility level does not match server version",
            Category = Category,
            Severity = AuditSeverity.Medium,
            DatabaseObject = context.Snapshot.DatabaseName,
            Description = $"Database compatibility level {context.Snapshot.CompatibilityLevel} differs from server-default level {expectedLevel}.",
            Impact = "Older compatibility levels can miss optimizer improvements; mismatched behavior makes performance troubleshooting harder.",
            Recommendation = "Validate workload and set the database compatibility level to the current server default.",
            ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                AuditOperationRisk.Unknown,
                "Changing compatibility level can materially change query plans and should be planned."),
            FixScript = $"""
                -- RequiresServiceWindow: true
                -- Reason: Compatibility level changes can alter query plans.
                ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = {expectedLevel};
                """,
            Evidence =
            [
                new FindingEvidence("ServerProductVersion", context.Snapshot.ProductVersion),
                new FindingEvidence("CurrentCompatibilityLevel", context.Snapshot.CompatibilityLevel.ToString(CultureInfo.InvariantCulture)),
                new FindingEvidence("ExpectedCompatibilityLevel", expectedLevel.ToString(CultureInfo.InvariantCulture)),
            ],
        };

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>([finding]);
    }

    private static bool TryResolveExpectedCompatibilityLevel(string productVersion, out int expectedLevel)
    {
        expectedLevel = 0;
        if (string.IsNullOrWhiteSpace(productVersion))
        {
            return false;
        }

        var majorSegment = productVersion.Split('.', 2, StringSplitOptions.TrimEntries)[0];
        if (!int.TryParse(majorSegment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var majorVersion)
            || majorVersion <= 0)
        {
            return false;
        }

        expectedLevel = majorVersion * 10;
        return true;
    }
}

internal sealed class IdentityExhaustionCheck : IHealthCheck
{
    public string Id => "CAP-001";

    public string Title => "Identity key exhaustion risk";

    public string Category => "Capacity";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();

        foreach (var identity in context.Snapshot.IdentityColumns
                     .Where(i => i.UsagePercent >= (decimal)context.Options.IdentityUsageWarningPercent))
        {
            var tableName = SqlName.Table(identity.SchemaName, identity.TableName);
            var severity = identity.UsagePercent >= (decimal)context.Options.IdentityUsageCriticalPercent
                ? AuditSeverity.Critical
                : AuditSeverity.High;

            findings.Add(new AuditFinding
            {
                Id = $"CAP-001-{identity.ObjectId}-{SqlName.ObjectNameSuffix(identity.ColumnName)}",
                Title = "Identity column nearing max value",
                Category = Category,
                Severity = severity,
                DatabaseObject = $"{tableName}.[{identity.ColumnName}]",
                Description = "Identity usage is approaching the data type limit.",
                Impact = "Inserts can fail once identity reaches max value.",
                Recommendation = "Plan migration to larger key type or reseed strategy before exhaustion.",
                ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                    AuditOperationRisk.OfflineIndexBuild,
                    "Changing key data type usually requires coordinated schema/data migration."),
                FixScript = $"""
                    -- RequiresServiceWindow: true
                    -- Reason: Preventive fix usually requires key migration and coordinated deployment.
                    -- Manual action required for {tableName}.[{identity.ColumnName}] ({identity.DataType}).
                    -- Example immediate mitigation (use carefully):
                    -- DBCC CHECKIDENT ('{tableName}', RESEED, <safe_seed_value>);
                    """,
                Evidence =
                [
                    new FindingEvidence("UsagePercent", identity.UsagePercent.ToString("F2", CultureInfo.InvariantCulture)),
                    new FindingEvidence("LastValue", identity.LastValue?.ToString(CultureInfo.InvariantCulture) ?? "null"),
                    new FindingEvidence("MaxValue", identity.MaxValue.ToString(CultureInfo.InvariantCulture)),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class OverWideIndexKeyCheck : IHealthCheck
{
    public string Id => "IDX-008";

    public string Title => "Over-wide index key definitions";

    public string Category => "Indexes";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        const int warningKeyBytes = 900;
        const int highRiskBytes = 1700;
        const int warningColumnCount = 8;

        var findings = context.Snapshot.Indexes
            .Where(i => !i.IsDisabled && !i.IsHypothetical && (i.KeySizeBytes > warningKeyBytes || i.KeyColumnCount > warningColumnCount))
            .Select(index =>
            {
                var tableName = SqlName.Table(index.SchemaName, index.TableName);
                var severity = index.KeySizeBytes > highRiskBytes || index.KeyColumnCount > 16
                    ? AuditSeverity.High
                    : AuditSeverity.Medium;

                return new AuditFinding
                {
                    Id = $"IDX-008-{index.ObjectId}-{index.IndexId}",
                    Title = "Index key is wider than recommended",
                    Category = Category,
                    Severity = severity,
                    DatabaseObject = $"{tableName}.{SqlName.Index(index.IndexName)}",
                    Description = "Key columns are likely too wide for efficient b-tree navigation.",
                    Impact = "Large keys reduce fanout and increase storage and IO costs.",
                    Recommendation = "Move non-search columns to INCLUDE and keep key columns narrow.",
                    ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                        AuditOperationRisk.PotentiallyOnlineIndexBuild,
                        "Key redesign requires index rebuild or replacement."),
                    FixScript = $"""
                        -- RequiresServiceWindow: true
                        -- Reason: Key redesign requires drop/create or rebuild.
                        -- Manual redesign required for {SqlName.Index(index.IndexName)} on {tableName}.
                        -- Existing key: {index.KeyColumns}
                        -- Existing includes: {index.IncludedColumns}
                        """,
                    Evidence =
                    [
                        new FindingEvidence("KeySizeBytes", index.KeySizeBytes.ToString(CultureInfo.InvariantCulture)),
                        new FindingEvidence("KeyColumnCount", index.KeyColumnCount.ToString(CultureInfo.InvariantCulture)),
                    ],
                };
            })
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class ScanHeavyIndexCheck : IHealthCheck
{
    public string Id => "IDX-009";

    public string Title => "Scan-heavy indexes";

    public string Category => "Indexes";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        const long minReads = 1000;
        const int scanToSeekRatioThreshold = 10;

        var findings = new List<AuditFinding>();

        foreach (var usage in context.Snapshot.IndexUsage
            .Where(u => u.IndexId > 1
                        && (u.UserSeeks + u.UserScans + u.UserLookups) >= minReads
                        && u.UserScans > 0
                        && u.UserScans / (u.UserSeeks + 1L) > scanToSeekRatioThreshold))
        {
            var index = context.Snapshot.Indexes.FirstOrDefault(
                i => i.ObjectId == usage.ObjectId && i.IndexId == usage.IndexId);
            if (index is null)
            {
                continue;
            }

            var tableName = SqlName.Table(index.SchemaName, index.TableName);
            findings.Add(new AuditFinding
            {
                Id = $"IDX-009-{usage.ObjectId}-{usage.IndexId}",
                Title = "Non-clustered index is scan-heavy",
                Category = Category,
                Severity = AuditSeverity.Info,
                DatabaseObject = $"{tableName}.{SqlName.Index(index.IndexName)}",
                Description = $"Index '{index.IndexName}' has {usage.UserScans:N0} scans vs {usage.UserSeeks:N0} seeks (ratio {usage.UserScans / (usage.UserSeeks + 1L):N0}:1). High scan ratios on non-clustered indexes suggest missing columns in the index, poor column selectivity, or queries that should use the clustered index instead.",
                Impact = "Non-clustered index scans can be slower than clustered index scans for large ranges and may indicate a suboptimal query or index design.",
                Recommendation = "Analyze the queries driving scans. Consider adding covering columns, reviewing WHERE clause selectivity, or removing the index if scans are replacing clustered index access.",
                ServiceWindow = ServiceWindowAdvisor.No("Observational finding — no schema change required."),
                Evidence =
                [
                    new FindingEvidence("UserSeeks", usage.UserSeeks.ToString("N0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("UserScans", usage.UserScans.ToString("N0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("ScanToSeekRatio", (usage.UserScans / (usage.UserSeeks + 1L)).ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("KeyColumns", index.KeyColumns),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class WriteAmplificationIndexCheck : IHealthCheck
{
    public string Id => "IDX-010";

    public string Title => "Write-amplification indexes";

    public string Category => "Indexes";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        const long minUpdates = 10_000;
        const long maxReads = 10;

        var findings = new List<AuditFinding>();

        foreach (var usage in context.Snapshot.IndexUsage
            .Where(u => u.IndexId > 1
                        && u.UserUpdates >= minUpdates
                        && (u.UserSeeks + u.UserScans + u.UserLookups) <= maxReads))
        {
            var index = context.Snapshot.Indexes.FirstOrDefault(
                i => i.ObjectId == usage.ObjectId && i.IndexId == usage.IndexId);
            if (index is null)
            {
                continue;
            }

            var tableName = SqlName.Table(index.SchemaName, index.TableName);
            findings.Add(new AuditFinding
            {
                Id = $"IDX-010-{usage.ObjectId}-{usage.IndexId}",
                Title = "Index has high writes with near-zero reads",
                Category = Category,
                Severity = AuditSeverity.Medium,
                DatabaseObject = $"{tableName}.{SqlName.Index(index.IndexName)}",
                Description = $"Index '{index.IndexName}' has been updated {usage.UserUpdates:N0} times but only read {usage.UserSeeks + usage.UserScans + usage.UserLookups:N0} times since last restart. This index is consuming write overhead without providing read benefit.",
                Impact = "Every INSERT, UPDATE, and DELETE on the table must also maintain this index, increasing write latency and log volume.",
                Recommendation = "Verify the index is not relied on by maintenance or one-off queries, then consider dropping it.",
                ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                    AuditOperationRisk.MetadataOnly,
                    "Dropping an index acquires a brief schema-modification lock."),
                FixScript = $"""
                    -- RequiresServiceWindow: false
                    -- Validate with a brief monitoring period before dropping.
                    DROP INDEX {SqlName.Index(index.IndexName)} ON {tableName};
                    """,
                Evidence =
                [
                    new FindingEvidence("UserUpdates", usage.UserUpdates.ToString("N0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("TotalReads", (usage.UserSeeks + usage.UserScans + usage.UserLookups).ToString("N0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("KeyColumns", index.KeyColumns),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class IntegrityCheckRecencyCheck : IHealthCheck
{
    public string Id => "MAINT-001";

    public string Title => "Database integrity check recency";

    public string Category => "Maintenance";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var lastCheck = context.Snapshot.LastDbccCheckDbUtc;

        if (lastCheck is null)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>(
            [
                new AuditFinding
                {
                    Id = "MAINT-001-UNKNOWN",
                    Title = "Database integrity check: history unavailable",
                    Category = Category,
                    Severity = AuditSeverity.Info,
                    DatabaseObject = context.Snapshot.DatabaseName,
                    Description = "DBCC CHECKDB history could not be read. Either the check has never been run, or permissions prevented reading DBCC DBINFO output.",
                    Impact = "Unknown. Silent database corruption may be present.",
                    Recommendation = "Schedule regular DBCC CHECKDB runs (at minimum weekly for non-critical, daily for critical databases).",
                    ServiceWindow = ServiceWindowAdvisor.No("Observational finding — no schema change required."),
                },
            ]);
        }

        var age = context.Snapshot.CapturedAtUtc - lastCheck.Value;

        AuditSeverity? severity = age.TotalDays switch
        {
            > 30 => AuditSeverity.High,
            > 7 => AuditSeverity.Medium,
            _ => null,
        };

        if (severity is null)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(
        [
            new AuditFinding
            {
                Id = "MAINT-001-STALE",
                Title = "DBCC CHECKDB has not run recently",
                Category = Category,
                Severity = severity.Value,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = $"The last known successful DBCC CHECKDB was {age.TotalDays:F0} days ago ({lastCheck.Value:yyyy-MM-dd}). Microsoft recommends running CHECKDB at least weekly.",
                Impact = "Database corruption can go undetected until it causes data loss or service interruption.",
                Recommendation = "Schedule a DBCC CHECKDB job. Use PHYSICAL_ONLY for daily/frequent runs; full CHECKDB weekly or on low-activity periods.",
                ServiceWindow = ServiceWindowAdvisor.No("Observational finding — no schema change required."),
                Evidence =
                [
                    new FindingEvidence("LastCheckDb", lastCheck.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    new FindingEvidence("AgeDays", age.TotalDays.ToString("F0", CultureInfo.InvariantCulture)),
                ],
            },
        ]);
    }
}

internal sealed class ColumnstoreOpportunityCheck : IHealthCheck
{
    public string Id => "IDX-011";

    public string Title => "Columnstore index opportunity on large scan-heavy table";

    public string Category => "Indexes";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();

        var tableObjectIds = context.Snapshot.Tables
            .Where(t => t.RowCount >= context.Options.LargeTableRowThreshold)
            .Select(t => t.ObjectId)
            .ToHashSet();

        var columnstoreObjectIds = context.Snapshot.Indexes
            .Where(i => i.IndexType.Contains("COLUMNSTORE", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.ObjectId)
            .ToHashSet();

        var usageByClustered = context.Snapshot.IndexUsage
            .Where(u => u.IndexId == 1)
            .ToDictionary(u => u.ObjectId);

        foreach (var table in context.Snapshot.Tables
            .Where(t => tableObjectIds.Contains(t.ObjectId) && !columnstoreObjectIds.Contains(t.ObjectId)))
        {
            if (!usageByClustered.TryGetValue(table.ObjectId, out var usage))
            {
                continue;
            }

            var totalReads = usage.UserSeeks + usage.UserScans + usage.UserLookups;
            if (totalReads == 0 || usage.UserScans == 0)
            {
                continue;
            }

            var scanRatio = (double)usage.UserScans / (double)(usage.UserSeeks + 1);
            if (scanRatio < 5.0)
            {
                continue;
            }

            var tableName = SqlName.Table(table.SchemaName, table.TableName);

            findings.Add(new AuditFinding
            {
                Id = $"IDX-011-{table.ObjectId}",
                Title = "Large table may benefit from a non-clustered columnstore index",
                Category = Category,
                Severity = AuditSeverity.Info,
                DatabaseObject = tableName,
                Description = $"Table '{tableName}' has {table.RowCount:N0} rows and its clustered index shows {usage.UserScans:N0} scans vs {usage.UserSeeks:N0} seeks (ratio {scanRatio:F1}:1), suggesting it is being range-scanned frequently. No columnstore index exists on this table.",
                Impact = "For analytical or reporting queries that scan large portions of a table, a non-clustered columnstore index can reduce query time by orders of magnitude through compression and batch-mode execution.",
                Recommendation = "Evaluate whether this table is accessed by analytical workloads. If so, consider adding a non-clustered columnstore index covering the frequently queried columns. Test on a non-production copy first.",
                ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                    AuditOperationRisk.PotentiallyOnlineIndexBuild,
                    "Building a columnstore index can be done ONLINE but may consume significant CPU and I/O on large tables."),
                FixScript = $"""
                    -- TODO: Replace <column_list> with the actual columns used in analytical queries.
                    -- Building online reduces blocking but requires Enterprise edition or SQL 2019+.
                    CREATE NONCLUSTERED COLUMNSTORE INDEX [NCCI_{SqlName.ObjectNameSuffix(table.TableName)}_Analytical]
                        ON {tableName} (<column_list>)
                        WITH (ONLINE = ON);
                    """,
                Evidence =
                [
                    new FindingEvidence("RowCount", table.RowCount.ToString("N0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("UserSeeks", usage.UserSeeks.ToString("N0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("UserScans", usage.UserScans.ToString("N0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("ScanRatio", scanRatio.ToString("F1", CultureInfo.InvariantCulture)),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}