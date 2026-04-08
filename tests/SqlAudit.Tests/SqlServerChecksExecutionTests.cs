using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;
using SqlAudit.SqlServer;

namespace SqlAudit.Tests;

public sealed class SqlServerChecksExecutionTests
{
    [Fact]
    public async Task MissingPrimaryKeyCheck_FlagsLargeTableAsHighSeverity()
    {
        var context = CreateContext(
            tables:
            [
                new TableInfo(1, "dbo", "Orders", 250_000, 1024m, HasPrimaryKey: false, IsHeap: false),
            ]);

        var findings = await ExecuteCheckAsync("PK-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.High, finding.Severity);
        Assert.Contains("ALTER TABLE [dbo].[Orders]", finding.FixScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompatibilityLevelCheck_FlagsServerVersionMismatch()
    {
        var context = CreateContext(productVersion: "16.0.1000.6", compatibilityLevel: 150);

        var findings = await ExecuteCheckAsync("CFG-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal("CFG-001-COMPATIBILITY-LEVEL", finding.Id);
        Assert.Contains("COMPATIBILITY_LEVEL = 160", finding.FixScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompatibilityLevelCheck_SkipsAzureDatabases()
    {
        var context = CreateContext(productVersion: "16.0", compatibilityLevel: 150, isAzureSql: true);

        var findings = await ExecuteCheckAsync("CFG-001", context);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task StatisticsConfigurationCheck_FlagsAutoOptionsAndNoRecompute()
    {
        var context = CreateContext(
            autoCreateStatisticsOn: false,
            autoUpdateStatisticsOn: false,
            statistics:
            [
                new StatisticsInfo(
                    ObjectId: 5,
                    StatsId: 2,
                    SchemaName: "dbo",
                    TableName: "Books",
                    StatsName: "_WA_Sys_00000001_12345",
                    IsAutoCreated: true,
                    IsNoRecompute: true,
                    LastUpdatedUtc: null,
                    Rows: 10_000,
                    ModificationCounter: 10),
            ]);

        var findings = await ExecuteCheckAsync("STAT-002", context);

        Assert.Equal(3, findings.Count);
        Assert.Contains(findings, f => string.Equals(f.Id, "STAT-002-AUTO-CREATE", StringComparison.Ordinal));
        Assert.Contains(findings, f => string.Equals(f.Id, "STAT-002-AUTO-UPDATE", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Id.StartsWith("STAT-002-NORECOMP-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FragmentationCheck_EmitsRebuildAndReorganizeFixes()
    {
        var context = CreateContext(
            indexes:
            [
                CreateIndex(10, 1, "IX_Books_Title"),
                CreateIndex(10, 2, "IX_Books_Author"),
            ],
            physicalStats:
            [
                new IndexPhysicalInfo(10, 1, PageCount: 2000, FragmentationPercent: 35.2, AvgPageSpaceUsedPercent: 90),
                new IndexPhysicalInfo(10, 2, PageCount: 2000, FragmentationPercent: 15.6, AvgPageSpaceUsedPercent: 90),
            ]);

        var findings = await ExecuteCheckAsync("IDX-005", context);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Severity == AuditSeverity.High && f.FixScript!.Contains("REBUILD", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Severity == AuditSeverity.Medium && f.FixScript!.Contains("REORGANIZE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnusedIndexCheck_FlagsOnlyLowReadHighWriteIndexes()
    {
        var context = CreateContext(
            indexes:
            [
                CreateIndex(42, 1, "IX_Keep", keyColumns: "[A]"),
                CreateIndex(42, 2, "IX_Used", keyColumns: "[B]"),
            ],
            usage:
            [
                new IndexUsageInfo(42, 1, UserSeeks: 1, UserScans: 1, UserLookups: 0, UserUpdates: 20_000, LastReadUtc: null),
                new IndexUsageInfo(42, 2, UserSeeks: 500, UserScans: 10, UserLookups: 0, UserUpdates: 20_000, LastReadUtc: null),
            ]);

        var findings = await ExecuteCheckAsync("IDX-004", context);

        var finding = Assert.Single(findings);
        Assert.Contains("IX_Keep", finding.DatabaseObject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OverlappingIndexCheck_FlagsNarrowIndexCoveredByWiderIndex()
    {
        var context = CreateContext(
            indexes:
            [
                CreateIndex(7, 1, "IX_Narrow", keyColumns: "[A]", includedColumns: "[B]"),
                CreateIndex(7, 2, "IX_Wide", keyColumns: "[A],[C]", includedColumns: "[B],[D]"),
            ]);

        var findings = await ExecuteCheckAsync("IDX-002", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.Low, finding.Severity);
        Assert.Contains("IX_Narrow", finding.Description, StringComparison.Ordinal);
        Assert.Contains("IX_Wide", finding.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdentityExhaustionCheck_UsesCriticalSeverityAboveCriticalThreshold()
    {
        var context = CreateContext(
            identityColumns:
            [
                new IdentityColumnInfo(88, "dbo", "Events", "EventId", "int", LastValue: 2_100_000_000m, MaxValue: 2_147_483_647m, UsagePercent: 97m),
            ]);

        var findings = await ExecuteCheckAsync("CAP-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.Critical, finding.Severity);
    }

    [Fact]
    public async Task OverWideIndexKeyCheck_UsesMediumAndHighSeverities()
    {
        var context = CreateContext(
            indexes:
            [
                CreateIndex(90, 1, "IX_MediumWide", keySizeBytes: 950, keyColumnCount: 4),
                CreateIndex(90, 2, "IX_HighWide", keySizeBytes: 1800, keyColumnCount: 10),
            ]);

        var findings = await ExecuteCheckAsync("IDX-008", context);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.DatabaseObject.Contains("IX_MediumWide", StringComparison.Ordinal) && f.Severity == AuditSeverity.Medium);
        Assert.Contains(findings, f => f.DatabaseObject.Contains("IX_HighWide", StringComparison.Ordinal) && f.Severity == AuditSeverity.High);
    }

    [Fact]
    public async Task ForeignKeyDisabledOrUntrustedCheck_FlagsInvalidConstraintState()
    {
        var context = CreateContext(
            foreignKeys:
            [
                new ForeignKeyInfo(9, "FK_Books_Authors", "dbo", "Books", "dbo", "Authors", "[AuthorId]", "[Id]", "int", "int", IsDisabled: true, IsNotTrusted: false, HasSupportingIndex: true, "NO_ACTION", "NO_ACTION"),
            ]);

        var finding = Assert.Single(await ExecuteCheckAsync("FK-001", context));
        Assert.Equal(AuditSeverity.High, finding.Severity);
        Assert.Contains("CHECK CONSTRAINT", finding.FixScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForeignKeyWithoutIndexCheck_FlagsMissingSupport()
    {
        var context = CreateContext(
            foreignKeys:
            [
                new ForeignKeyInfo(11, "FK_Books_Authors", "dbo", "Books", "dbo", "Authors", "[AuthorId]", "[Id]", "int", "int", IsDisabled: false, IsNotTrusted: false, HasSupportingIndex: false, "NO_ACTION", "NO_ACTION"),
            ]);

        var finding = Assert.Single(await ExecuteCheckAsync("FK-002", context));
        Assert.Equal(AuditSeverity.Medium, finding.Severity);
        Assert.Contains("CREATE INDEX", finding.FixScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForeignKeyTypeMismatchCheck_FlagsDifferingTypeSignatures()
    {
        var context = CreateContext(
            foreignKeys:
            [
                new ForeignKeyInfo(12, "FK_Books_Authors", "dbo", "Books", "dbo", "Authors", "[AuthorId]", "[Id]", "bigint", "int", IsDisabled: false, IsNotTrusted: false, HasSupportingIndex: true, "NO_ACTION", "NO_ACTION"),
            ]);

        var finding = Assert.Single(await ExecuteCheckAsync("FK-003", context));
        Assert.Equal(AuditSeverity.High, finding.Severity);
        Assert.Contains("Parent types: bigint", finding.FixScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateIndexCheck_FlagsRedundantDefinitions()
    {
        var context = CreateContext(
            indexes:
            [
                CreateIndex(21, 1, "IX_A", keyColumns: "[A]", includedColumns: "[B]"),
                CreateIndex(21, 2, "IX_B", keyColumns: "[A]", includedColumns: "[B]"),
            ]);

        var finding = Assert.Single(await ExecuteCheckAsync("IDX-001", context));
        Assert.Equal(AuditSeverity.Medium, finding.Severity);
        Assert.Contains("DROP INDEX", finding.FixScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledIndexCheck_FlagsDisabledIndexes()
    {
        var disabled = CreateIndex(30, 1, "IX_Disabled") with { IsDisabled = true };
        var context = CreateContext(indexes: [disabled]);

        var finding = Assert.Single(await ExecuteCheckAsync("IDX-003", context));
        Assert.Equal(AuditSeverity.Medium, finding.Severity);
        Assert.Contains("REBUILD", finding.FixScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LowPageDensityCheck_FlagsIndexesBelowThreshold()
    {
        var context = CreateContext(
            indexes: [CreateIndex(41, 1, "IX_LowDensity")],
            physicalStats:
            [
                new IndexPhysicalInfo(41, 1, PageCount: 2000, FragmentationPercent: 5, AvgPageSpaceUsedPercent: 60),
            ]);

        var finding = Assert.Single(await ExecuteCheckAsync("IDX-006", context));
        Assert.Equal(AuditSeverity.Medium, finding.Severity);
    }

    [Fact]
    public async Task FillFactorAnomalyCheck_FlagsVeryLowFillFactor()
    {
        var lowFill = CreateIndex(51, 1, "IX_LowFill") with { FillFactor = 60 };
        var context = CreateContext(indexes: [lowFill]);

        var finding = Assert.Single(await ExecuteCheckAsync("IDX-007", context));
        Assert.Equal(AuditSeverity.Low, finding.Severity);
        Assert.Contains("FILLFACTOR = 90", finding.FixScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaleStatisticsCheck_FlagsRowsAboveModificationThreshold()
    {
        var context = CreateContext(
            statistics:
            [
                new StatisticsInfo(
                    ObjectId: 61,
                    StatsId: 1,
                    SchemaName: "dbo",
                    TableName: "Books",
                    StatsName: "IX_Stats",
                    IsAutoCreated: false,
                    IsNoRecompute: false,
                    LastUpdatedUtc: null,
                    Rows: 10_000,
                    ModificationCounter: 3_000),
            ]);

        var finding = Assert.Single(await ExecuteCheckAsync("STAT-001", context));
        Assert.Contains("UPDATE STATISTICS", finding.FixScript, StringComparison.Ordinal);
        Assert.Equal(AuditSeverity.Medium, finding.Severity);
    }

    private static async Task<IReadOnlyCollection<AuditFinding>> ExecuteCheckAsync(string checkId, HealthCheckContext context)
    {
        var check = SqlServerHealthChecks.CreateDeep().Single(c => string.Equals(c.Id, checkId, StringComparison.OrdinalIgnoreCase));
        return await check.ExecuteAsync(context, CancellationToken.None);
    }

    private static HealthCheckContext CreateContext(
        IReadOnlyList<TableInfo>? tables = null,
        IReadOnlyList<IndexInfo>? indexes = null,
        IReadOnlyList<IndexUsageInfo>? usage = null,
        IReadOnlyList<IndexPhysicalInfo>? physicalStats = null,
        IReadOnlyList<ForeignKeyInfo>? foreignKeys = null,
        IReadOnlyList<StatisticsInfo>? statistics = null,
        IReadOnlyList<IdentityColumnInfo>? identityColumns = null,
        IReadOnlyList<ColumnInfo>? columns = null,
        IReadOnlyList<ColumnNullStats>? columnNullStats = null,
        IReadOnlyList<WaitStatInfo>? waitStats = null,
        bool autoCreateStatisticsOn = true,
        bool autoUpdateStatisticsOn = true,
        string productVersion = "16.0",
        int compatibilityLevel = 160,
        bool isAzureSql = false)
    {
        return new HealthCheckContext
        {
            Snapshot = new DatabaseSnapshot
            {
                ServerName = "server01",
                DatabaseName = "DbA",
                Edition = "Developer",
                ProductVersion = productVersion,
                CompatibilityLevel = compatibilityLevel,
                IsAzureSql = isAzureSql,
                AutoCreateStatisticsOn = autoCreateStatisticsOn,
                AutoUpdateStatisticsOn = autoUpdateStatisticsOn,
                Tables = tables ?? [],
                Indexes = indexes ?? [],
                IndexUsage = usage ?? [],
                IndexPhysicalStats = physicalStats ?? [],
                ForeignKeys = foreignKeys ?? [],
                Statistics = statistics ?? [],
                IdentityColumns = identityColumns ?? [],
                Columns = columns ?? [],
                ColumnNullStats = columnNullStats ?? [],
                TopWaitStats = waitStats ?? [],
            },
            Options = AuditOptions.Default,
        };
    }

    [Fact]
    public async Task DominantWaitCategoryCheck_FlagsCategoryAbove50Percent()
    {
        var context = CreateContext(waitStats:
        [
            new WaitStatInfo("PAGEIOLATCH_SH", 1000, WaitTimeSeconds: 80m, ResourceWaitSeconds: 80m, SignalWaitSeconds: 0m, AverageWaitMs: 80m, Category: "I/O"),
            new WaitStatInfo("LCK_M_X",        200,  WaitTimeSeconds: 20m, ResourceWaitSeconds: 20m, SignalWaitSeconds: 0m, AverageWaitMs: 10m, Category: "Locking"),
        ]);

        var findings = await ExecuteCheckAsync("WAIT-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal("WAIT-001-I/O", finding.Id);
        Assert.Contains("I/O", finding.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DominantWaitCategoryCheck_NoFindingWhenNoCategoryDominates()
    {
        var context = CreateContext(waitStats:
        [
            new WaitStatInfo("PAGEIOLATCH_SH", 500, WaitTimeSeconds: 40m, ResourceWaitSeconds: 40m, SignalWaitSeconds: 0m, AverageWaitMs: 8m, Category: "I/O"),
            new WaitStatInfo("LCK_M_X",        500, WaitTimeSeconds: 35m, ResourceWaitSeconds: 35m, SignalWaitSeconds: 0m, AverageWaitMs: 7m, Category: "Locking"),
            new WaitStatInfo("SOS_SCHEDULER",  300, WaitTimeSeconds: 25m, ResourceWaitSeconds: 10m, SignalWaitSeconds: 15m, AverageWaitMs: 8m, Category: "CPU/Scheduler"),
        ]);

        var findings = await ExecuteCheckAsync("WAIT-001", context);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task CpuPressureCheck_FlagsHighSignalWaitRatio()
    {
        var context = CreateContext(waitStats:
        [
            new WaitStatInfo("SOS_SCHEDULER_YIELD", 5000, WaitTimeSeconds: 60m, ResourceWaitSeconds: 20m, SignalWaitSeconds: 40m, AverageWaitMs: 12m, Category: "CPU/Scheduler"),
            new WaitStatInfo("PAGEIOLATCH_SH",       500,  WaitTimeSeconds: 40m, ResourceWaitSeconds: 38m, SignalWaitSeconds: 2m,  AverageWaitMs: 8m,  Category: "I/O"),
        ]);

        var findings = await ExecuteCheckAsync("WAIT-002", context);

        var finding = Assert.Single(findings);
        Assert.Equal("WAIT-002-CPU-PRESSURE", finding.Id);
        Assert.Equal(AuditSeverity.High, finding.Severity);
    }

    [Fact]
    public async Task CpuPressureCheck_NoFindingWhenSignalWaitLow()
    {
        var context = CreateContext(waitStats:
        [
            new WaitStatInfo("PAGEIOLATCH_SH", 1000, WaitTimeSeconds: 100m, ResourceWaitSeconds: 98m, SignalWaitSeconds: 2m, AverageWaitMs: 10m, Category: "I/O"),
        ]);

        var findings = await ExecuteCheckAsync("WAIT-002", context);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task NullableColumnWithNoNullsCheck_FlagsNullStatEntries()
    {
        var context = CreateContext(
            tables: [new TableInfo(1, "dbo", "Orders", 50_000, 100m, HasPrimaryKey: true, IsHeap: false)],
            columnNullStats:
            [
                new ColumnNullStats(1, "dbo", "Orders", "Notes"),
                new ColumnNullStats(1, "dbo", "Orders", "ShippedDate"),
            ]);

        var findings = await ExecuteCheckAsync("COL-001", context);

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(AuditSeverity.Info, f.Severity));
        Assert.Contains(findings, f => f.DatabaseObject.Contains("Notes", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.DatabaseObject.Contains("ShippedDate", StringComparison.Ordinal));
        Assert.All(findings, f => Assert.Contains("NOT NULL", f.FixScript, StringComparison.Ordinal));
    }

    [Fact]
    public async Task OversizedStringColumnCheck_FlagsNvarcharMaxAndWideColumns()
    {
        var context = CreateContext(columns:
        [
            new ColumnInfo(1, "dbo", "Orders", "Notes",       "nvarchar", MaxLength: -1,   IsNullable: true,  ColumnId: 1),
            new ColumnInfo(1, "dbo", "Orders", "Description", "varchar",  MaxLength: 8000, IsNullable: false, ColumnId: 2),
            new ColumnInfo(1, "dbo", "Orders", "Code",        "nvarchar", MaxLength: 20,   IsNullable: false, ColumnId: 3),
        ]);

        var findings = await ExecuteCheckAsync("COL-002", context);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.DatabaseObject.Contains("Notes",       StringComparison.Ordinal) && f.Severity == AuditSeverity.Medium);
        Assert.Contains(findings, f => f.DatabaseObject.Contains("Description", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, f => f.DatabaseObject.Contains("Code", StringComparison.Ordinal));
    }

    private static IndexInfo CreateIndex(
        int objectId,
        int indexId,
        string indexName,
        string keyColumns = "[Id]",
        string includedColumns = "",
        int keySizeBytes = 200,
        int keyColumnCount = 1)
    {
        return new IndexInfo(
            ObjectId: objectId,
            IndexId: indexId,
            SchemaName: "dbo",
            TableName: "Books",
            IndexName: indexName,
            IndexType: "NONCLUSTERED",
            IsUnique: false,
            IsPrimaryKey: false,
            IsUniqueConstraint: false,
            IsDisabled: false,
            IsHypothetical: false,
            FillFactor: 90,
            KeyColumns: keyColumns,
            IncludedColumns: includedColumns,
            HasFilter: false,
            FilterDefinition: null,
            KeySizeBytes: keySizeBytes,
            KeyColumnCount: keyColumnCount);
    }
}
