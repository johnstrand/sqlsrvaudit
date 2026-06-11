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
            ],
            autoCreateStatisticsOn: false,
            autoUpdateStatisticsOn: false);

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
        IReadOnlyList<ServerConfigInfo>? serverConfigurations = null,
        IReadOnlyList<SleepingTransactionInfo>? sleepingTransactions = null,
        IReadOnlyList<FileIoLatencyInfo>? fileIoLatency = null,
        IReadOnlyList<TableCompressionInfo>? tableCompression = null,
        IReadOnlyList<BlockingSessionInfo>? blockingSessions = null,
        IReadOnlyList<VolumeInfo>? volumeStats = null,
        IReadOnlyList<FailedAgentJobInfo>? failedAgentJobs = null,
        IReadOnlyList<GlobalTraceFlagInfo>? globalTraceFlags = null,
        IReadOnlyList<SecurityHygieneIssueInfo>? securityHygieneIssues = null,
        TempDbConfigInfo? tempDbConfig = null,
        MemoryPressureInfo? memoryPressure = null,
        PlanCacheInfo? planCache = null,
        BackupPostureInfo? backupPosture = null,
        LogHealthInfo? logHealth = null,
        DatabaseOptionsInfo? databaseOptions = null,
        DateTimeOffset? lastDbccCheckDbUtc = null,
        DateTimeOffset? capturedAtUtc = null,
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
                ServerConfigurations = serverConfigurations ?? [],
                SleepingTransactions = sleepingTransactions ?? [],
                FileIoLatency = fileIoLatency ?? [],
                TableCompression = tableCompression ?? [],
                ActiveBlockingSessions = blockingSessions ?? [],
                VolumeStats = volumeStats ?? [],
                FailedAgentJobs = failedAgentJobs ?? [],
                GlobalTraceFlags = globalTraceFlags ?? [],
                SecurityHygieneIssues = securityHygieneIssues ?? [],
                TempDbConfig = tempDbConfig,
                MemoryPressure = memoryPressure,
                PlanCache = planCache,
                BackupPosture = backupPosture,
                LogHealth = logHealth,
                DatabaseOptions = databaseOptions,
                LastDbccCheckDbUtc = lastDbccCheckDbUtc,
                CapturedAtUtc = capturedAtUtc ?? DateTimeOffset.UtcNow,
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
                new ColumnNullStats(1, "dbo", "Orders", "Notes", "nvarchar(100)"),
                new ColumnNullStats(1, "dbo", "Orders", "ShippedDate", "datetime2(7)"),
            ]);

        var findings = await ExecuteCheckAsync("COL-001", context);

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(AuditSeverity.Info, f.Severity));
        Assert.Contains(findings, f => f.DatabaseObject.Contains("Notes", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.DatabaseObject.Contains("ShippedDate", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.FixScript != null && f.FixScript.Contains("nvarchar(100) NOT NULL", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.FixScript != null && f.FixScript.Contains("datetime2(7) NOT NULL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OversizedStringColumnCheck_FlagsNvarcharMaxAndWideColumns()
    {
        var context = CreateContext(columns:
        [
            new ColumnInfo(1, "dbo", "Orders", "Notes",       "nvarchar", MaxLength: -1,   Precision: 0, Scale: 0, IsNullable: true,  ColumnId: 1),
            new ColumnInfo(1, "dbo", "Orders", "Description", "varchar",  MaxLength: 8000, Precision: 0, Scale: 0, IsNullable: false, ColumnId: 2),
            new ColumnInfo(1, "dbo", "Orders", "Code",        "nvarchar", MaxLength: 20,   Precision: 0, Scale: 0, IsNullable: false, ColumnId: 3),
        ]);

        var findings = await ExecuteCheckAsync("COL-002", context);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.DatabaseObject.Contains("Notes", StringComparison.Ordinal) && f.Severity == AuditSeverity.Medium);
        Assert.Contains(findings, f => f.DatabaseObject.Contains("Description", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, f => f.DatabaseObject.Contains("Code", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MaxDopConfigurationCheck_FlagsMagDopZero()
    {
        var context = CreateContext(serverConfigurations:
        [
            new ServerConfigInfo("max degree of parallelism", 0, "max degree of parallelism"),
        ]);

        var findings = await ExecuteCheckAsync("CFG-002", context);

        var finding = Assert.Single(findings);
        Assert.Equal("CFG-002-MAXDOP-ZERO", finding.Id);
        Assert.Equal(AuditSeverity.Medium, finding.Severity);
    }

    [Fact]
    public async Task MaxDopConfigurationCheck_NoFindingWhenMaxdopIsSet()
    {
        var context = CreateContext(serverConfigurations:
        [
            new ServerConfigInfo("max degree of parallelism", 8, "max degree of parallelism"),
        ]);

        var findings = await ExecuteCheckAsync("CFG-002", context);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task CostThresholdForParallelismCheck_FlagsDefaultValue()
    {
        var context = CreateContext(serverConfigurations:
        [
            new ServerConfigInfo("cost threshold for parallelism", 5, "cost threshold for parallelism"),
        ]);

        var findings = await ExecuteCheckAsync("CFG-003", context);

        Assert.Single(findings);
        Assert.Equal(AuditSeverity.Medium, Assert.Single(findings).Severity);
    }

    [Fact]
    public async Task OptimizeForAdHocWorkloadsCheck_FlagsWhenDisabled()
    {
        var context = CreateContext(serverConfigurations:
        [
            new ServerConfigInfo("optimize for ad hoc workloads", 0, "optimize for ad hoc workloads"),
        ]);

        var findings = await ExecuteCheckAsync("CFG-004", context);

        Assert.Equal("CFG-004-ADHOC-OFF", Assert.Single(findings).Id);
    }

    [Fact]
    public async Task MaxServerMemoryCheck_FlagsUnlimitedDefault()
    {
        var context = CreateContext(serverConfigurations:
        [
            new ServerConfigInfo("max server memory (MB)", 2147483647, "max server memory"),
        ]);

        var findings = await ExecuteCheckAsync("CFG-005", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.High, finding.Severity);
    }

    [Fact]
    public async Task TempDbFileCountCheck_FlagsWhenFewerFilesThanCpus()
    {
        var context = CreateContext(tempDbConfig: new TempDbConfigInfo(DataFileCount: 1, LogicalCpuCount: 8, DataFileSizesMb: [8m]));

        var findings = await ExecuteCheckAsync("TMPDB-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.Medium, finding.Severity);
        Assert.Contains("1", finding.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TempDbFileSizeEqualityCheck_FlagsUnequalFiles()
    {
        var context = CreateContext(tempDbConfig: new TempDbConfigInfo(DataFileCount: 4, LogicalCpuCount: 8, DataFileSizesMb: [8m, 8m, 8m, 1000m]));

        var findings = await ExecuteCheckAsync("TMPDB-002", context);

        Assert.Equal(AuditSeverity.Low, Assert.Single(findings).Severity);
    }

    [Fact]
    public async Task TempDbFileSizeEqualityCheck_NoFindingWhenFilesEqual()
    {
        var context = CreateContext(tempDbConfig: new TempDbConfigInfo(DataFileCount: 4, LogicalCpuCount: 8, DataFileSizesMb: [512m, 512m, 512m, 512m]));

        var findings = await ExecuteCheckAsync("TMPDB-002", context);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task SleepingOpenTransactionCheck_FlagsLongIdleSession()
    {
        var context = CreateContext(sleepingTransactions:
        [
            new SleepingTransactionInfo(SessionId: 55, LoginName: "AppUser", DatabaseName: "DbA", OpenTransactionCount: 1, ElapsedMinutes: 45m, LastQueryText: "SELECT 1"),
        ]);

        var findings = await ExecuteCheckAsync("SESS-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.High, finding.Severity);
        Assert.Contains("55", finding.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SleepingOpenTransactionCheck_NoFindingForRecentSession()
    {
        var context = CreateContext(sleepingTransactions:
        [
            new SleepingTransactionInfo(SessionId: 55, LoginName: "AppUser", DatabaseName: "DbA", OpenTransactionCount: 1, ElapsedMinutes: 1m, LastQueryText: "SELECT 1"),
        ]);

        var findings = await ExecuteCheckAsync("SESS-001", context);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task PageLifeExpectancyCheck_FlagsLowPle()
    {
        var context = CreateContext(memoryPressure: new MemoryPressureInfo(
            PageLifeExpectancySeconds: 120,
            BufferCacheHitRatioPercent: 98m,
            TotalServerMemoryMb: 4096m,
            TargetServerMemoryMb: 4096m));

        var findings = await ExecuteCheckAsync("MEM-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.High, finding.Severity);
    }

    [Fact]
    public async Task PageLifeExpectancyCheck_NoFindingForHealthyServer()
    {
        var context = CreateContext(memoryPressure: new MemoryPressureInfo(
            PageLifeExpectancySeconds: 5000,
            BufferCacheHitRatioPercent: 99.5m,
            TotalServerMemoryMb: 32768m,
            TargetServerMemoryMb: 32768m));

        var findings = await ExecuteCheckAsync("MEM-001", context);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task SingleUsePlanRatioCheck_FlagsHighAdHocRatio()
    {
        var context = CreateContext(planCache: new PlanCacheInfo(
            TotalCachedPlans: 1000,
            SingleUsePlans: 750,
            SingleUsePlanPercent: 75m,
            CacheSizeMb: 512m,
            AdHocCacheSizeMb: 350m));

        var findings = await ExecuteCheckAsync("CACHE-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.Medium, finding.Severity);
    }

    [Fact]
    public async Task DataFileReadLatencyCheck_FlagsHighLatency()
    {
        var context = CreateContext(fileIoLatency:
        [
            new FileIoLatencyInfo(DatabaseId: 1, FileId: 1, LogicalName: "mydb_data", FileType: "ROWS", ReadIoCount: 10000, WriteIoCount: 5000, AvgReadLatencyMs: 60m, AvgWriteLatencyMs: 2m, SizeMb: 1024m),
        ]);

        var findings = await ExecuteCheckAsync("IO-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.High, finding.Severity);
        Assert.Contains("mydb_data", finding.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogFileWriteLatencyCheck_FlagsHighLatency()
    {
        var context = CreateContext(fileIoLatency:
        [
            new FileIoLatencyInfo(DatabaseId: 1, FileId: 2, LogicalName: "mydb_log", FileType: "LOG", ReadIoCount: 100, WriteIoCount: 50000, AvgReadLatencyMs: 0m, AvgWriteLatencyMs: 25m, SizeMb: 256m),
        ]);

        var findings = await ExecuteCheckAsync("IO-002", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.High, finding.Severity);
    }

    [Fact]
    public async Task IntegrityCheckRecencyCheck_FlagsStaleCheckDb()
    {
        var lastCheck = DateTimeOffset.UtcNow.AddDays(-15);
        var context = CreateContext(lastDbccCheckDbUtc: lastCheck);

        var findings = await ExecuteCheckAsync("MAINT-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal("MAINT-001-STALE", finding.Id);
        Assert.Equal(AuditSeverity.Medium, finding.Severity);
    }

    [Fact]
    public async Task IntegrityCheckRecencyCheck_FlagsNullAsInfo()
    {
        var context = CreateContext();

        var findings = await ExecuteCheckAsync("MAINT-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal("MAINT-001-UNKNOWN", finding.Id);
        Assert.Equal(AuditSeverity.Info, finding.Severity);
    }

    [Fact]
    public async Task ScanHeavyIndexCheck_FlagsHighScanToSeekRatio()
    {
        var index = CreateIndex(objectId: 1, indexId: 2, indexName: "IX_Orders_Status");
        var usageRecord = new IndexUsageInfo(
            ObjectId: 1,
            IndexId: 2,
            UserSeeks: 10,
            UserScans: 5000,
            UserLookups: 0,
            UserUpdates: 500,
            LastReadUtc: null);
        var context = CreateContext(indexes: [index], usage: [usageRecord]);

        var findings = await ExecuteCheckAsync("IDX-009", context);

        Assert.Equal(AuditSeverity.Info, Assert.Single(findings).Severity);
    }

    [Fact]
    public async Task WriteAmplificationIndexCheck_FlagsHighWriteLowReadIndex()
    {
        var index = CreateIndex(objectId: 1, indexId: 2, indexName: "IX_Orders_Junk");
        var usageRecord = new IndexUsageInfo(
            ObjectId: 1,
            IndexId: 2,
            UserSeeks: 0,
            UserScans: 5,
            UserLookups: 0,
            UserUpdates: 50_000,
            LastReadUtc: null);
        var context = CreateContext(indexes: [index], usage: [usageRecord]);

        var findings = await ExecuteCheckAsync("IDX-010", context);

        Assert.Equal(AuditSeverity.Medium, Assert.Single(findings).Severity);
    }

    [Fact]
    public async Task UncompressedLargeTableCheck_FlagsLargeUncompressedTable()
    {
        var context = CreateContext(
            tables:
            [
                new TableInfo(1, "dbo", "Orders", 1_000_000, 200m, HasPrimaryKey: true, IsHeap: false),
            ],
            tableCompression:
            [
                new TableCompressionInfo(ObjectId: 1, SchemaName: "dbo", TableName: "Orders", PartitionNumber: 1, DataCompression: "NONE", Rows: 1_000_000, UsedPageCount: 50_000),
            ]);

        var findings = await ExecuteCheckAsync("COMP-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.Info, finding.Severity);
        Assert.Contains("REBUILD", finding.FixScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HighVlfCountCheck_FlagsHighVlfCount()
    {
        var context = CreateContext(logHealth: new LogHealthInfo(
            TotalLogSizeMb: 1024m, UsedLogSizeMb: 200m, UsedLogPercent: 19.5m,
            VlfCount: 1500, LongestActiveTransactionMinutes: 0, LogReuseWaitDescription: "NOTHING"));

        var findings = await ExecuteCheckAsync("LOG-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.High, finding.Severity);
        Assert.Contains("1500", finding.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HighVlfCountCheck_NoFindingWhenVlfCountOk()
    {
        var context = CreateContext(logHealth: new LogHealthInfo(
            TotalLogSizeMb: 512m, UsedLogSizeMb: 50m, UsedLogPercent: 9.8m,
            VlfCount: 50, LongestActiveTransactionMinutes: 0, LogReuseWaitDescription: "NOTHING"));

        var findings = await ExecuteCheckAsync("LOG-001", context);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task LogReuseWaitCheck_FlagsActiveTransaction()
    {
        var context = CreateContext(logHealth: new LogHealthInfo(
            TotalLogSizeMb: 512m, UsedLogSizeMb: 500m, UsedLogPercent: 97.7m,
            VlfCount: 100, LongestActiveTransactionMinutes: 60, LogReuseWaitDescription: "ACTIVE_TRANSACTION"));

        var findings = await ExecuteCheckAsync("LOG-002", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.High, finding.Severity);
        Assert.Contains("ACTIVE_TRANSACTION", finding.Id, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogReuseWaitCheck_NoFindingForLogBackup()
    {
        var context = CreateContext(logHealth: new LogHealthInfo(
            TotalLogSizeMb: 512m, UsedLogSizeMb: 200m, UsedLogPercent: 39.1m,
            VlfCount: 100, LongestActiveTransactionMinutes: 0, LogReuseWaitDescription: "LOG_BACKUP"));

        var findings = await ExecuteCheckAsync("LOG-002", context);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task FullBackupRecencyCheck_FlagsNeverBackedUp()
    {
        var context = CreateContext(backupPosture: new BackupPostureInfo(
            RecoveryModel: "FULL",
            LastFullBackupUtc: null,
            LastDifferentialBackupUtc: null,
            LastLogBackupUtc: null,
            FullBackupAgeHours: null,
            DifferentialBackupAgeHours: null,
            LogBackupAgeHours: null));

        var findings = await ExecuteCheckAsync("BAK-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.Critical, finding.Severity);
    }

    [Fact]
    public async Task FullBackupRecencyCheck_NoFindingForRecentBackup()
    {
        var context = CreateContext(backupPosture: new BackupPostureInfo(
            RecoveryModel: "FULL",
            LastFullBackupUtc: DateTimeOffset.UtcNow.AddHours(-24),
            LastDifferentialBackupUtc: null,
            LastLogBackupUtc: DateTimeOffset.UtcNow.AddMinutes(-30),
            FullBackupAgeHours: 24m,
            DifferentialBackupAgeHours: null,
            LogBackupAgeHours: 0.5m));

        var findings = await ExecuteCheckAsync("BAK-001", context);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task LogBackupForFullRecoveryCheck_FlagsNoLogBackups()
    {
        var context = CreateContext(backupPosture: new BackupPostureInfo(
            RecoveryModel: "FULL",
            LastFullBackupUtc: DateTimeOffset.UtcNow.AddDays(-1),
            LastDifferentialBackupUtc: null,
            LastLogBackupUtc: null,
            FullBackupAgeHours: 24m,
            DifferentialBackupAgeHours: null,
            LogBackupAgeHours: null));

        var findings = await ExecuteCheckAsync("BAK-002", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.Critical, finding.Severity);
    }

    [Fact]
    public async Task LogBackupForFullRecoveryCheck_NoFindingForSimpleRecovery()
    {
        var context = CreateContext(backupPosture: new BackupPostureInfo(
            RecoveryModel: "SIMPLE",
            LastFullBackupUtc: DateTimeOffset.UtcNow.AddDays(-1),
            LastDifferentialBackupUtc: null,
            LastLogBackupUtc: null,
            FullBackupAgeHours: 24m,
            DifferentialBackupAgeHours: null,
            LogBackupAgeHours: null));

        var findings = await ExecuteCheckAsync("BAK-002", context);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task DifferentialBackupGapCheck_FlagsMissingDifferentialBackup()
    {
        var context = CreateContext(backupPosture: new BackupPostureInfo(
            RecoveryModel: "FULL",
            LastFullBackupUtc: DateTimeOffset.UtcNow.AddHours(-48),
            LastDifferentialBackupUtc: null,
            LastLogBackupUtc: DateTimeOffset.UtcNow.AddMinutes(-30),
            FullBackupAgeHours: 48m,
            DifferentialBackupAgeHours: null,
            LogBackupAgeHours: 0.5m));

        var findings = await ExecuteCheckAsync("BAK-003", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.Low, finding.Severity);
        Assert.Contains("The last full backup was 48 hours ago", finding.Description, StringComparison.Ordinal);
        Assert.Contains("never", finding.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DifferentialBackupGapCheck_FlagsOldDifferentialBackup()
    {
        var context = CreateContext(backupPosture: new BackupPostureInfo(
            RecoveryModel: "FULL",
            LastFullBackupUtc: DateTimeOffset.UtcNow.AddHours(-100),
            LastDifferentialBackupUtc: DateTimeOffset.UtcNow.AddHours(-80),
            LastLogBackupUtc: DateTimeOffset.UtcNow.AddMinutes(-30),
            FullBackupAgeHours: 100m,
            DifferentialBackupAgeHours: 80m,
            LogBackupAgeHours: 0.5m));

        var findings = await ExecuteCheckAsync("BAK-003", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.Low, finding.Severity);
        Assert.Contains("The last full backup was 100 hours ago", finding.Description, StringComparison.Ordinal);
        Assert.Contains("80 hours ago", finding.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DifferentialBackupGapCheck_NoFindingForRecentFullBackup()
    {
        var context = CreateContext(backupPosture: new BackupPostureInfo(
            RecoveryModel: "FULL",
            LastFullBackupUtc: DateTimeOffset.UtcNow.AddHours(-12),
            LastDifferentialBackupUtc: null,
            LastLogBackupUtc: DateTimeOffset.UtcNow.AddMinutes(-30),
            FullBackupAgeHours: 12m,
            DifferentialBackupAgeHours: null,
            LogBackupAgeHours: 0.5m));

        var findings = await ExecuteCheckAsync("BAK-003", context);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task DifferentialBackupGapCheck_NoFindingForRecentDifferentialBackup()
    {
        var context = CreateContext(backupPosture: new BackupPostureInfo(
            RecoveryModel: "FULL",
            LastFullBackupUtc: DateTimeOffset.UtcNow.AddHours(-48),
            LastDifferentialBackupUtc: DateTimeOffset.UtcNow.AddHours(-24),
            LastLogBackupUtc: DateTimeOffset.UtcNow.AddMinutes(-30),
            FullBackupAgeHours: 48m,
            DifferentialBackupAgeHours: 24m,
            LogBackupAgeHours: 0.5m));

        var findings = await ExecuteCheckAsync("BAK-003", context);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task AutoShrinkCheck_FlagsAutoShrinkEnabled()
    {
        var context = CreateContext(databaseOptions: new DatabaseOptionsInfo(
            AutoShrink: true, AutoClose: false,
            PageVerify: "CHECKSUM", IsRcsiEnabled: true,
            QueryStoreEnabled: true, QueryStoreState: "READ_WRITE"));

        var findings = await ExecuteCheckAsync("DB-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.High, finding.Severity);
        Assert.Contains("AUTO_SHRINK OFF", finding.FixScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutoCloseCheck_FlagsAutoCloseEnabled()
    {
        var context = CreateContext(databaseOptions: new DatabaseOptionsInfo(
            AutoShrink: false, AutoClose: true,
            PageVerify: "CHECKSUM", IsRcsiEnabled: true,
            QueryStoreEnabled: true, QueryStoreState: "READ_WRITE"));

        var findings = await ExecuteCheckAsync("DB-002", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.Medium, finding.Severity);
        Assert.Contains("AUTO_CLOSE OFF", finding.FixScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PageVerifyCheck_FlagsNonChecksum()
    {
        var context = CreateContext(databaseOptions: new DatabaseOptionsInfo(
            AutoShrink: false, AutoClose: false,
            PageVerify: "TORN_PAGE_DETECTION", IsRcsiEnabled: true,
            QueryStoreEnabled: true, QueryStoreState: "READ_WRITE"));

        var findings = await ExecuteCheckAsync("DB-003", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.High, finding.Severity);
        Assert.Contains("CHECKSUM", finding.FixScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryStoreDisabledCheck_FlagsDisabledState_With10OrMoreTables()
    {
        var tables = Enumerable.Range(1, 10).Select(i => new TableInfo(i, "dbo", $"Table{i}", 100, 1m, true, false)).ToList();
        var context = CreateContext(
            tables: tables,
            databaseOptions: new DatabaseOptionsInfo(
                AutoShrink: false, AutoClose: false,
                PageVerify: "CHECKSUM", IsRcsiEnabled: true,
                QueryStoreEnabled: false, QueryStoreState: "OFF"));

        var findings = await ExecuteCheckAsync("DB-005", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.Low, finding.Severity);
    }

    [Fact]
    public async Task QueryStoreDisabledCheck_NoFinding_WhenEnabled()
    {
        var tables = Enumerable.Range(1, 10).Select(i => new TableInfo(i, "dbo", $"Table{i}", 100, 1m, true, false)).ToList();
        var context = CreateContext(
            tables: tables,
            databaseOptions: new DatabaseOptionsInfo(
                AutoShrink: false, AutoClose: false,
                PageVerify: "CHECKSUM", IsRcsiEnabled: true,
                QueryStoreEnabled: true, QueryStoreState: "READ_WRITE"));

        var findings = await ExecuteCheckAsync("DB-005", context);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task QueryStoreDisabledCheck_NoFinding_WithFewerThan10Tables()
    {
        var tables = Enumerable.Range(1, 9).Select(i => new TableInfo(i, "dbo", $"Table{i}", 100, 1m, true, false)).ToList();
        var context = CreateContext(
            tables: tables,
            databaseOptions: new DatabaseOptionsInfo(
                AutoShrink: false, AutoClose: false,
                PageVerify: "CHECKSUM", IsRcsiEnabled: true,
                QueryStoreEnabled: false, QueryStoreState: "OFF"));

        var findings = await ExecuteCheckAsync("DB-005", context);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task QueryStoreReadOnlyCheck_FlagsReadOnlyState()
    {
        var context = CreateContext(databaseOptions: new DatabaseOptionsInfo(
            AutoShrink: false, AutoClose: false,
            PageVerify: "CHECKSUM", IsRcsiEnabled: true,
            QueryStoreEnabled: true, QueryStoreState: "READ_ONLY"));

        var findings = await ExecuteCheckAsync("DB-006", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.Medium, finding.Severity);
    }

    [Fact]
    public async Task LowDiskSpaceCheck_FlagsLowAvailableSpace()
    {
        var context = CreateContext(volumeStats:
        [
            new VolumeInfo(VolumeMount: "D:\\", TotalBytes: 100L * 1024 * 1024 * 1024, AvailableBytes: 3L * 1024 * 1024 * 1024, AvailablePercent: 3m, LogicalName: "mydb_data", FileType: "ROWS"),
        ]);

        var findings = await ExecuteCheckAsync("STOR-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.High, finding.Severity);
    }

    [Fact]
    public async Task DataAndLogOnSameVolumeCheck_FlagsMixedVolume()
    {
        var context = CreateContext(volumeStats:
        [
            new VolumeInfo(VolumeMount: "D:\\", TotalBytes: 100L * 1024 * 1024 * 1024, AvailableBytes: 50L * 1024 * 1024 * 1024, AvailablePercent: 50m, LogicalName: "mydb_data", FileType: "ROWS"),
            new VolumeInfo(VolumeMount: "D:\\", TotalBytes: 100L * 1024 * 1024 * 1024, AvailableBytes: 50L * 1024 * 1024 * 1024, AvailablePercent: 50m, LogicalName: "mydb_log", FileType: "LOG"),
        ]);

        var findings = await ExecuteCheckAsync("STOR-002", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.Low, finding.Severity);
    }

    [Fact]
    public async Task FailedAgentJobsCheck_FlagsRecentFailure()
    {
        var context = CreateContext(
            failedAgentJobs:
            [
                new FailedAgentJobInfo(
                    JobName: "Weekly Index Rebuild",
                    StepName: "Rebuild Indexes",
                    LastRunUtc: DateTimeOffset.UtcNow.AddHours(-12),
                    ErrorMessage: "The step failed because it could not obtain a lock.",
                    RunDurationSeconds: 300),
            ],
            capturedAtUtc: DateTimeOffset.UtcNow);

        var findings = await ExecuteCheckAsync("MAINT-002", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.High, finding.Severity);
        Assert.Contains("Weekly Index Rebuild", finding.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HarmfulTraceFlagCheck_FlagsKnownHarmfulFlag()
    {
        var context = CreateContext(globalTraceFlags:
        [
            new GlobalTraceFlagInfo(TraceFlag: 3625, IsGlobal: true),
        ]);

        var findings = await ExecuteCheckAsync("CFG-006", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.Medium, finding.Severity);
        Assert.Contains("3625", finding.Id, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HarmfulTraceFlagCheck_NoFindingForSafeFlag()
    {
        var context = CreateContext(globalTraceFlags:
        [
            new GlobalTraceFlagInfo(TraceFlag: 1222, IsGlobal: true),
        ]);

        var findings = await ExecuteCheckAsync("CFG-006", context);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task ColumnstoreOpportunityCheck_FlagsScanHeavyLargeTable()
    {
        var table = new TableInfo(1, "dbo", "SalesHistory", 1_000_000, 2048m, HasPrimaryKey: true, IsHeap: false);
        var clusteredIndex = new IndexInfo(
            ObjectId: 1, IndexId: 1, SchemaName: "dbo", TableName: "SalesHistory",
            IndexName: "PK_SalesHistory", IndexType: "CLUSTERED",
            IsUnique: true, IsPrimaryKey: true, IsUniqueConstraint: false,
            IsDisabled: false, IsHypothetical: false, FillFactor: 90,
            KeyColumns: "[Id]", IncludedColumns: string.Empty,
            HasFilter: false, FilterDefinition: null,
            KeySizeBytes: 8, KeyColumnCount: 1);
        var usage = new IndexUsageInfo(
            ObjectId: 1, IndexId: 1, UserSeeks: 100, UserScans: 5000, UserLookups: 0, UserUpdates: 200, LastReadUtc: null);

        var context = CreateContext(
            tables: [table],
            indexes: [clusteredIndex],
            usage: [usage]);

        var findings = await ExecuteCheckAsync("IDX-011", context);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditSeverity.Info, finding.Severity);
        Assert.Contains("COLUMNSTORE", finding.FixScript, StringComparison.Ordinal);
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
    [Fact]
    public async Task SecurityHygieneCheck_ReturnsFindings_WhenIssuesExist()
    {
        var context = CreateContext(securityHygieneIssues:
        [
            new SecurityHygieneIssueInfo("OrphanUser", AuditSeverity.High, "test_user", "Orphaned user detected")
        ]);

        var findings = await ExecuteCheckAsync("SEC-001", context);

        var finding = Assert.Single(findings);
        Assert.Equal("SEC-001-ORPHANUSER-TEST_USER", finding.Id);
        Assert.Equal(AuditSeverity.High, finding.Severity);
        Assert.Equal("test_user", finding.DatabaseObject);
        Assert.Contains("Orphaned user detected", finding.Description, StringComparison.Ordinal);
    }
}
