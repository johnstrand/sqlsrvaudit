namespace SqlAudit.Core.Models;

public sealed class DatabaseSnapshot
{
    public required string ServerName { get; init; }

    public required string DatabaseName { get; init; }

    public required string Edition { get; init; }

    public required string ProductVersion { get; init; }

    public required bool IsAzureSql { get; init; }

    public required bool AutoCreateStatisticsOn { get; init; }

    public required bool AutoUpdateStatisticsOn { get; init; }

    public required IReadOnlyList<TableInfo> Tables { get; init; }

    public required IReadOnlyList<IndexInfo> Indexes { get; init; }

    public required IReadOnlyList<IndexUsageInfo> IndexUsage { get; init; }

    public required IReadOnlyList<IndexPhysicalInfo> IndexPhysicalStats { get; init; }

    public required IReadOnlyList<ForeignKeyInfo> ForeignKeys { get; init; }

    public required IReadOnlyList<StatisticsInfo> Statistics { get; init; }

    public required IReadOnlyList<IdentityColumnInfo> IdentityColumns { get; init; }
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
