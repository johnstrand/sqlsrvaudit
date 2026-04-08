namespace SqlAudit.Core.Models;

public sealed class DatabaseSnapshot
{
    public DateTimeOffset CapturedAtUtc { get; init; }

    public required string ServerName { get; init; }

    public required string DatabaseName { get; init; }

    public required string Edition { get; init; }

    public required string ProductVersion { get; init; }

    public required int CompatibilityLevel { get; init; }

    public required bool IsAzureSql { get; init; }

    /// <summary>
    /// True for Enterprise, Developer, and Azure SQL editions, which support ONLINE = ON for index and table rebuilds.
    /// Standard and Express editions require ONLINE = OFF.
    /// </summary>
    public bool SupportsOnlineIndexOperations =>
        IsAzureSql ||
        Edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase) ||
        Edition.Contains("Developer", StringComparison.OrdinalIgnoreCase);

    public required bool AutoCreateStatisticsOn { get; init; }

    public required bool AutoUpdateStatisticsOn { get; init; }

    public required IReadOnlyList<TableInfo> Tables { get; init; }

    public required IReadOnlyList<IndexInfo> Indexes { get; init; }

    public required IReadOnlyList<IndexUsageInfo> IndexUsage { get; init; }

    public required IReadOnlyList<IndexPhysicalInfo> IndexPhysicalStats { get; init; }

    public required IReadOnlyList<ForeignKeyInfo> ForeignKeys { get; init; }

    public required IReadOnlyList<StatisticsInfo> Statistics { get; init; }

    public required IReadOnlyList<IdentityColumnInfo> IdentityColumns { get; init; }

    public IReadOnlyList<ResourceIntensiveQueryInfo> TopResourceIntensiveQueries { get; init; } = [];

    public IReadOnlyList<WaitStatInfo> TopWaitStats { get; init; } = [];

    public IReadOnlyList<QueryStoreRegressionInfo> QueryStoreRegressions { get; init; } = [];

    public IReadOnlyList<BlockingSessionInfo> ActiveBlockingSessions { get; init; } = [];

    public DeadlockSummaryInfo? DeadlockSummary { get; init; }

    public IReadOnlyList<MissingIndexSignalInfo> MissingIndexSignals { get; init; } = [];

    public LogHealthInfo? LogHealth { get; init; }

    public TempDbPressureInfo? TempDbPressure { get; init; }

    public IReadOnlyList<FileGrowthHealthInfo> FileGrowthHealth { get; init; } = [];

    public BackupPostureInfo? BackupPosture { get; init; }

    public IReadOnlyList<SecurityHygieneIssueInfo> SecurityHygieneIssues { get; init; } = [];

    public IReadOnlyList<CollectionWarning> CollectionWarnings { get; init; } = [];

    public IReadOnlyList<ColumnInfo> Columns { get; init; } = [];

    public IReadOnlyList<ColumnNullStats> ColumnNullStats { get; init; } = [];

    public IReadOnlyList<ServerConfigInfo> ServerConfigurations { get; init; } = [];

    public DateTimeOffset? LastDbccCheckDbUtc { get; init; }

    public TempDbConfigInfo? TempDbConfig { get; init; }

    public IReadOnlyList<SleepingTransactionInfo> SleepingTransactions { get; init; } = [];

    public MemoryPressureInfo? MemoryPressure { get; init; }

    public IReadOnlyList<FileIoLatencyInfo> FileIoLatency { get; init; } = [];

    public PlanCacheInfo? PlanCache { get; init; }

    public IReadOnlyList<TableCompressionInfo> TableCompression { get; init; } = [];

    public DatabaseOptionsInfo? DatabaseOptions { get; init; }

    public IReadOnlyList<VolumeInfo> VolumeStats { get; init; } = [];

    public IReadOnlyList<FailedAgentJobInfo> FailedAgentJobs { get; init; } = [];

    public IReadOnlyList<GlobalTraceFlagInfo> GlobalTraceFlags { get; init; } = [];
}

public sealed record TableInfo(
    int ObjectId,
    string SchemaName,
    string TableName,
    long RowCount,
    decimal ReservedMb,
    bool HasPrimaryKey,
    bool IsHeap);

public sealed record IndexInfo(
    int ObjectId,
    int IndexId,
    string SchemaName,
    string TableName,
    string IndexName,
    string IndexType,
    bool IsUnique,
    bool IsPrimaryKey,
    bool IsUniqueConstraint,
    bool IsDisabled,
    bool IsHypothetical,
    int FillFactor,
    string KeyColumns,
    string IncludedColumns,
    bool HasFilter,
    string? FilterDefinition,
    int KeySizeBytes,
    int KeyColumnCount);

public sealed record IndexUsageInfo(
    int ObjectId,
    int IndexId,
    long UserSeeks,
    long UserScans,
    long UserLookups,
    long UserUpdates,
    DateTimeOffset? LastReadUtc);

public sealed record IndexPhysicalInfo(
    int ObjectId,
    int IndexId,
    long PageCount,
    double FragmentationPercent,
    double AvgPageSpaceUsedPercent);

public sealed record ForeignKeyInfo(
    int ObjectId,
    string ForeignKeyName,
    string ParentSchema,
    string ParentTable,
    string ReferencedSchema,
    string ReferencedTable,
    string ParentColumns,
    string ReferencedColumns,
    string ParentColumnTypes,
    string ReferencedColumnTypes,
    bool IsDisabled,
    bool IsNotTrusted,
    bool HasSupportingIndex,
    string DeleteAction,
    string UpdateAction);

public sealed record StatisticsInfo(
    int ObjectId,
    int StatsId,
    string SchemaName,
    string TableName,
    string StatsName,
    bool IsAutoCreated,
    bool IsNoRecompute,
    DateTimeOffset? LastUpdatedUtc,
    long Rows,
    long ModificationCounter);

public sealed record IdentityColumnInfo(
    int ObjectId,
    string SchemaName,
    string TableName,
    string ColumnName,
    string DataType,
    decimal? LastValue,
    decimal MaxValue,
    decimal UsagePercent);

public sealed record ResourceIntensiveQueryInfo(
    string QueryHash,
    long ExecutionCount,
    decimal TotalCpuMs,
    decimal AverageCpuMs,
    decimal TotalDurationMs,
    decimal AverageDurationMs,
    long TotalLogicalReads,
    long TotalLogicalWrites,
    DateTimeOffset? LastExecutionUtc,
    string QueryText);

public sealed record WaitStatInfo(
    string WaitType,
    long WaitingTasksCount,
    decimal WaitTimeSeconds,
    decimal ResourceWaitSeconds,
    decimal SignalWaitSeconds,
    decimal AverageWaitMs,
    string Category);

public sealed record QueryStoreRegressionInfo(
    long QueryId,
    decimal BaselineAverageDurationMs,
    decimal RecentAverageDurationMs,
    decimal RegressionRatio,
    long RecentExecutions,
    DateTimeOffset? LastExecutionUtc,
    string QueryText);

public sealed record BlockingSessionInfo(
    int BlockingSessionId,
    int BlockedSessionId,
    string WaitType,
    long WaitDurationMs,
    string WaitResource,
    string QueryText);

public sealed record DeadlockSummaryInfo(
    long DeadlockCountLast24Hours,
    DateTimeOffset? LastDeadlockUtc);

public sealed record MissingIndexSignalInfo(
    int ObjectId,
    string SchemaName,
    string TableName,
    string EqualityColumns,
    string InequalityColumns,
    string IncludedColumns,
    long UserSeeks,
    long UserScans,
    decimal AverageTotalCost,
    decimal AverageUserImpactPercent,
    decimal EstimatedBenefit,
    int ExistingIndexCount,
    string GuardrailNote);

public sealed record LogHealthInfo(
    decimal TotalLogSizeMb,
    decimal UsedLogSizeMb,
    decimal UsedLogPercent,
    int VlfCount,
    long LongestActiveTransactionMinutes,
    string LogReuseWaitDescription);

public sealed record TempDbPressureInfo(
    decimal VersionStoreMb,
    decimal UserObjectMb,
    decimal InternalObjectMb,
    decimal UnallocatedMb);

public sealed record FileGrowthHealthInfo(
    int FileId,
    string LogicalName,
    string FileType,
    string PhysicalPath,
    decimal SizeMb,
    bool IsPercentGrowth,
    decimal GrowthValue,
    decimal? MaxSizeMb,
    string GrowthDescription,
    string Advisory);

public sealed record BackupPostureInfo(
    string RecoveryModel,
    DateTimeOffset? LastFullBackupUtc,
    DateTimeOffset? LastDifferentialBackupUtc,
    DateTimeOffset? LastLogBackupUtc,
    decimal? FullBackupAgeHours,
    decimal? DifferentialBackupAgeHours,
    decimal? LogBackupAgeHours);

public sealed record SecurityHygieneIssueInfo(
    string IssueType,
    AuditSeverity Severity,
    string Principal,
    string Details);

public sealed record TableGrowthForecastInfo(
    string DatabaseObject,
    decimal PreviousReservedMb,
    decimal CurrentReservedMb,
    decimal DeltaReservedMb,
    decimal ElapsedDays,
    decimal Projected30DayReservedMb,
    decimal Projected90DayReservedMb);

public sealed record CollectionWarning(string Section, string Reason);

public sealed record ColumnInfo(
    int ObjectId,
    string SchemaName,
    string TableName,
    string ColumnName,
    string DataType,
    int MaxLength,
    bool IsNullable,
    int ColumnId);

public sealed record ColumnNullStats(
    int ObjectId,
    string SchemaName,
    string TableName,
    string ColumnName);

public sealed record CollectionProgress(string StepName, int Completed, int Total);

public sealed record ServerConfigInfo(string Name, decimal ValueInUse, string Description);

public sealed record TempDbConfigInfo(int DataFileCount, int LogicalCpuCount, IReadOnlyList<decimal> DataFileSizesMb);

public sealed record SleepingTransactionInfo(
    int SessionId,
    string LoginName,
    string DatabaseName,
    int OpenTransactionCount,
    decimal ElapsedMinutes,
    string LastQueryText);

public sealed record MemoryPressureInfo(
    long PageLifeExpectancySeconds,
    decimal BufferCacheHitRatioPercent,
    decimal TotalServerMemoryMb,
    decimal TargetServerMemoryMb);

public sealed record FileIoLatencyInfo(
    int DatabaseId,
    int FileId,
    string LogicalName,
    string FileType,
    long ReadIoCount,
    long WriteIoCount,
    decimal AvgReadLatencyMs,
    decimal AvgWriteLatencyMs,
    decimal SizeMb);

public sealed record PlanCacheInfo(
    int TotalCachedPlans,
    int SingleUsePlans,
    decimal SingleUsePlanPercent,
    decimal CacheSizeMb,
    decimal AdHocCacheSizeMb);

public sealed record TableCompressionInfo(
    int ObjectId,
    string SchemaName,
    string TableName,
    int PartitionNumber,
    string DataCompression,
    long Rows,
    long UsedPageCount);

public sealed record DatabaseOptionsInfo(
    bool AutoShrink,
    bool AutoClose,
    string PageVerify,
    bool IsRcsiEnabled,
    bool QueryStoreEnabled,
    string QueryStoreState);

public sealed record VolumeInfo(
    string VolumeMount,
    long TotalBytes,
    long AvailableBytes,
    decimal AvailablePercent,
    string LogicalName,
    string FileType);

public sealed record FailedAgentJobInfo(
    string JobName,
    string StepName,
    DateTimeOffset LastRunUtc,
    string ErrorMessage,
    int RunDurationSeconds);

public sealed record GlobalTraceFlagInfo(int TraceFlag, bool IsGlobal);
