using Microsoft.Data.SqlClient;
using SqlAudit.Core.Models;
using System.Globalization;

namespace SqlAudit.SqlServer;

public sealed class SqlServerSnapshotCollector
{
    public static async Task<DatabaseSnapshot> CollectAsync(
        string connectionString,
        AuditProfile profile,
        IReadOnlyCollection<string>? excludedSchemas,
        IReadOnlyCollection<string>? excludedTables,
        CancellationToken cancellationToken,
        IProgress<CollectionProgress>? progress = null)
    {
        var totalSteps = profile == AuditProfile.Deep ? 33 : 29;
        var completed = 0;

        void Report(string stepName)
            => progress?.Report(new CollectionProgress(stepName, ++completed, totalSteps));

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var warnings = new List<CollectionWarning>();

        Report("Server information");
        var serverInfo = await ReadServerInfoAsync(connection, cancellationToken).ConfigureAwait(false);
        var includePhysical = profile == AuditProfile.Deep;
        var includeStatistics = profile == AuditProfile.Deep;

        Report("Tables");
        var tables = await ReadTablesAsync(connection, cancellationToken).ConfigureAwait(false);
        Report("Indexes");
        var indexes = await ReadIndexesAsync(connection, cancellationToken).ConfigureAwait(false);

        var hasStatePermission = await HasStateReadPermissionAsync(connection, cancellationToken).ConfigureAwait(false);
        if (!hasStatePermission)
        {
            warnings.Add(new CollectionWarning(
                "Dynamic Management Views",
                "The account lacks VIEW SERVER STATE or VIEW DATABASE STATE permission. " +
                "Resource-intensive queries, wait statistics, query store regressions, active blocking sessions, " +
                "deadlock summary, missing index signals, log health, and tempdb pressure data were not collected."));
        }

        Report("Resource-intensive queries");
        var topResourceIntensiveQueries = await TryReadOptionalListAsync(
            () => ReadTopResourceIntensiveQueriesAsync(connection, cancellationToken),
            warnings,
            "Resource-intensive queries").ConfigureAwait(false);
        Report("Wait statistics");
        var topWaitStats = await TryReadOptionalListAsync(
            () => ReadTopWaitStatsAsync(connection, cancellationToken),
            warnings,
            "Wait statistics").ConfigureAwait(false);
        Report("Query Store regressions");
        var queryStoreRegressions = await TryReadOptionalListAsync(
            () => ReadQueryStoreRegressionsAsync(connection, cancellationToken),
            warnings,
            "Query Store regressions").ConfigureAwait(false);
        Report("Active blocking sessions");
        var activeBlockingSessions = await TryReadOptionalListAsync(
            () => ReadActiveBlockingSessionsAsync(connection, cancellationToken),
            warnings,
            "Active blocking sessions").ConfigureAwait(false);
        Report("Deadlock summary");
        var deadlockSummary = await TryReadOptionalAsync(
            () => ReadDeadlockSummaryAsync(connection, cancellationToken),
            warnings,
            "Deadlock summary").ConfigureAwait(false);
        Report("Missing index signals");
        var missingIndexSignals = await TryReadOptionalListAsync(
            () => ReadMissingIndexSignalsAsync(connection, cancellationToken),
            warnings,
            "Missing index signals").ConfigureAwait(false);
        Report("Log health");
        var logHealth = await TryReadOptionalAsync(
            () => ReadLogHealthAsync(connection, cancellationToken),
            warnings,
            "Log health").ConfigureAwait(false);
        Report("TempDB pressure");
        var tempDbPressure = await TryReadOptionalAsync(
            () => ReadTempDbPressureAsync(connection, cancellationToken),
            warnings,
            "TempDB pressure").ConfigureAwait(false);
        Report("File growth settings");
        var fileGrowthHealth = await TryReadOptionalListAsync(
            () => ReadFileGrowthHealthAsync(connection, cancellationToken),
            warnings,
            "File growth settings").ConfigureAwait(false);
        Report("Backup posture");
        var backupPosture = await TryReadOptionalAsync(
            () => ReadBackupPostureAsync(connection, cancellationToken),
            warnings,
            "Backup posture").ConfigureAwait(false);
        Report("Security hygiene");
        var securityHygieneIssues = await TryReadOptionalListAsync(
            () => ReadSecurityHygieneIssuesAsync(connection, cancellationToken),
            warnings,
            "Security hygiene").ConfigureAwait(false);

        Report("Index usage statistics");
        var indexUsage = await TryReadOptionalListAsync(
            () => ReadIndexUsageAsync(connection, cancellationToken),
            warnings,
            "Index Usage Statistics").ConfigureAwait(false);

        Report("Index physical stats");
        var indexPhysicalStats = includePhysical
            ? await TryReadOptionalListAsync(
                () => ReadIndexPhysicalStatsAsync(connection, cancellationToken),
                warnings,
                "Index Physical Statistics").ConfigureAwait(false)
            : (IReadOnlyList<IndexPhysicalInfo>)[];

        Report("Table statistics");
        var statistics = includeStatistics
            ? await TryReadOptionalListAsync(
                () => ReadStatisticsAsync(connection, cancellationToken),
                warnings,
                "Table Statistics").ConfigureAwait(false)
            : (IReadOnlyList<StatisticsInfo>)[];

        Report("Column metadata");
        var columns = await TryReadOptionalListAsync(
            () => ReadColumnsAsync(connection, cancellationToken),
            warnings,
            "Column Metadata").ConfigureAwait(false);

        Report("Column null statistics");
        var columnNullStats = profile == AuditProfile.Deep
            ? await TryReadOptionalListAsync(
                () => ReadColumnNullStatsAsync(connection, columns, tables, cancellationToken),
                warnings,
                "Column Null Statistics").ConfigureAwait(false)
            : (IReadOnlyList<ColumnNullStats>)[];

        Report("Foreign keys");
        var foreignKeys = await ReadForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
        Report("Identity columns");
        var identityColumns = await ReadIdentityColumnsAsync(connection, cancellationToken).ConfigureAwait(false);

        Report("Server configurations");
        var serverConfigurations = await TryReadOptionalListAsync(
            () => ReadServerConfigurationsAsync(connection, cancellationToken),
            warnings,
            "Server Configurations").ConfigureAwait(false);

        Report("Integrity check history");
        var lastDbccCheckDb = await TryReadOptionalStructAsync(
            () => ReadLastDbccCheckDbAsync(connection, cancellationToken),
            warnings,
            "Integrity check history").ConfigureAwait(false);

        Report("TempDB configuration");
        var tempDbConfig = await TryReadOptionalAsync(
            () => ReadTempDbConfigAsync(connection, cancellationToken),
            warnings,
            "TempDB configuration").ConfigureAwait(false);

        Report("Sleeping transactions");
        var sleepingTransactions = await TryReadOptionalListAsync(
            () => ReadSleepingTransactionsAsync(connection, cancellationToken),
            warnings,
            "Sleeping Transactions").ConfigureAwait(false);

        Report("Memory pressure");
        var memoryPressure = await TryReadOptionalAsync(
            () => ReadMemoryPressureAsync(connection, cancellationToken),
            warnings,
            "Memory pressure").ConfigureAwait(false);

        Report("File I/O latency");
        var fileIoLatency = await TryReadOptionalListAsync(
            () => ReadFileIoLatencyAsync(connection, cancellationToken),
            warnings,
            "File I/O Latency").ConfigureAwait(false);

        Report("Plan cache");
        var planCache = await TryReadOptionalAsync(
            () => ReadPlanCacheAsync(connection, cancellationToken),
            warnings,
            "Plan cache").ConfigureAwait(false);

        Report("Table compression");
        var tableCompression = profile == AuditProfile.Deep
            ? await TryReadOptionalListAsync(
                () => ReadTableCompressionAsync(connection, cancellationToken),
                warnings,
                "Table Compression").ConfigureAwait(false)
            : (IReadOnlyList<TableCompressionInfo>)[];

        Report("Database options");
        var databaseOptions = await TryReadOptionalAsync(
            () => ReadDatabaseOptionsAsync(connection, cancellationToken),
            warnings,
            "Database options").ConfigureAwait(false);

        Report("Volume stats");
        var volumeStats = await TryReadOptionalListAsync(
            () => ReadVolumeStatsAsync(connection, cancellationToken),
            warnings,
            "Volume Stats").ConfigureAwait(false);

        Report("SQL Agent job failures");
        var failedAgentJobs = await TryReadOptionalListAsync(
            () => ReadFailedAgentJobsAsync(connection, cancellationToken),
            warnings,
            "SQL Agent Jobs").ConfigureAwait(false);

        Report("Global trace flags");
        var globalTraceFlags = await TryReadOptionalListAsync(
            () => ReadGlobalTraceFlagsAsync(connection, cancellationToken),
            warnings,
            "Global trace flags").ConfigureAwait(false);

        var snapshot = new DatabaseSnapshot
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            ServerName = serverInfo.ServerName,
            DatabaseName = serverInfo.DatabaseName,
            Edition = serverInfo.Edition,
            ProductVersion = serverInfo.ProductVersion,
            CompatibilityLevel = serverInfo.CompatibilityLevel,
            IsAzureSql = serverInfo.IsAzureSql,
            AutoCreateStatisticsOn = serverInfo.AutoCreateStatisticsOn,
            AutoUpdateStatisticsOn = serverInfo.AutoUpdateStatisticsOn,
            Tables = tables,
            Indexes = indexes,
            IndexUsage = indexUsage,
            IndexPhysicalStats = indexPhysicalStats,
            ForeignKeys = foreignKeys,
            Statistics = statistics,
            IdentityColumns = identityColumns,
            TopResourceIntensiveQueries = topResourceIntensiveQueries,
            TopWaitStats = topWaitStats,
            QueryStoreRegressions = queryStoreRegressions,
            ActiveBlockingSessions = activeBlockingSessions,
            DeadlockSummary = deadlockSummary,
            MissingIndexSignals = missingIndexSignals,
            LogHealth = logHealth,
            TempDbPressure = tempDbPressure,
            FileGrowthHealth = fileGrowthHealth,
            BackupPosture = backupPosture,
            SecurityHygieneIssues = securityHygieneIssues,
            CollectionWarnings = warnings,
            Columns = columns,
            ColumnNullStats = columnNullStats,
            ServerConfigurations = serverConfigurations,
            LastDbccCheckDbUtc = lastDbccCheckDb,
            TempDbConfig = tempDbConfig,
            SleepingTransactions = sleepingTransactions,
            MemoryPressure = memoryPressure,
            FileIoLatency = fileIoLatency,
            PlanCache = planCache,
            TableCompression = tableCompression,
            DatabaseOptions = databaseOptions,
            VolumeStats = volumeStats,
            FailedAgentJobs = failedAgentJobs,
            GlobalTraceFlags = globalTraceFlags,
        };

        return ApplyExclusions(snapshot, excludedSchemas, excludedTables);
    }

    private static DatabaseSnapshot ApplyExclusions(
        DatabaseSnapshot snapshot,
        IReadOnlyCollection<string>? excludedSchemas,
        IReadOnlyCollection<string>? excludedTables)
    {
        var excludedSchemaSet = excludedSchemas?
            .Where(schema => !string.IsNullOrWhiteSpace(schema))
            .Select(schema => schema.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excludedTableSet = excludedTables?
            .Where(table => !string.IsNullOrWhiteSpace(table))
            .Select(table => table.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if ((excludedSchemaSet is null || excludedSchemaSet.Count == 0)
            && (excludedTableSet is null || excludedTableSet.Count == 0))
        {
            return snapshot;
        }

        var tables = snapshot.Tables
            .Where(table => !IsExcludedTable(table, excludedSchemaSet, excludedTableSet))
            .ToArray();
        var tableIds = tables
            .Select(table => table.ObjectId)
            .ToHashSet();

        var indexes = snapshot.Indexes
            .Where(index => tableIds.Contains(index.ObjectId))
            .ToArray();
        var indexKeys = indexes
            .Select(index => (index.ObjectId, index.IndexId))
            .ToHashSet();

        return new DatabaseSnapshot
        {
            CapturedAtUtc = snapshot.CapturedAtUtc,
            ServerName = snapshot.ServerName,
            DatabaseName = snapshot.DatabaseName,
            Edition = snapshot.Edition,
            ProductVersion = snapshot.ProductVersion,
            CompatibilityLevel = snapshot.CompatibilityLevel,
            IsAzureSql = snapshot.IsAzureSql,
            AutoCreateStatisticsOn = snapshot.AutoCreateStatisticsOn,
            AutoUpdateStatisticsOn = snapshot.AutoUpdateStatisticsOn,
            Tables = tables,
            Indexes = indexes,
            IndexUsage = [.. snapshot.IndexUsage.Where(usage => indexKeys.Contains((usage.ObjectId, usage.IndexId)))],
            IndexPhysicalStats = [.. snapshot.IndexPhysicalStats.Where(stat => indexKeys.Contains((stat.ObjectId, stat.IndexId)))],
            ForeignKeys = [.. snapshot.ForeignKeys
                .Where(foreignKey =>
                    !IsExcludedTable(foreignKey.ParentSchema, foreignKey.ParentTable, excludedSchemaSet, excludedTableSet)
                    && !IsExcludedTable(foreignKey.ReferencedSchema, foreignKey.ReferencedTable, excludedSchemaSet, excludedTableSet)),],
            Statistics = [.. snapshot.Statistics.Where(stat => tableIds.Contains(stat.ObjectId))],
            IdentityColumns = [.. snapshot.IdentityColumns.Where(identity => tableIds.Contains(identity.ObjectId))],
            TopResourceIntensiveQueries = snapshot.TopResourceIntensiveQueries,
            TopWaitStats = snapshot.TopWaitStats,
            QueryStoreRegressions = snapshot.QueryStoreRegressions,
            ActiveBlockingSessions = snapshot.ActiveBlockingSessions,
            DeadlockSummary = snapshot.DeadlockSummary,
            MissingIndexSignals = [.. snapshot.MissingIndexSignals.Where(signal => tableIds.Contains(signal.ObjectId))],
            LogHealth = snapshot.LogHealth,
            TempDbPressure = snapshot.TempDbPressure,
            FileGrowthHealth = snapshot.FileGrowthHealth,
            BackupPosture = snapshot.BackupPosture,
            SecurityHygieneIssues = snapshot.SecurityHygieneIssues,
            CollectionWarnings = snapshot.CollectionWarnings,
            Columns = [.. snapshot.Columns.Where(c => tableIds.Contains(c.ObjectId))],
            ColumnNullStats = [.. snapshot.ColumnNullStats.Where(c => tableIds.Contains(c.ObjectId))],
            ServerConfigurations = snapshot.ServerConfigurations,
            LastDbccCheckDbUtc = snapshot.LastDbccCheckDbUtc,
            TempDbConfig = snapshot.TempDbConfig,
            SleepingTransactions = snapshot.SleepingTransactions,
            MemoryPressure = snapshot.MemoryPressure,
            FileIoLatency = snapshot.FileIoLatency,
            PlanCache = snapshot.PlanCache,
            TableCompression = [.. snapshot.TableCompression.Where(c => tableIds.Contains(c.ObjectId))],
            DatabaseOptions = snapshot.DatabaseOptions,
            VolumeStats = snapshot.VolumeStats,
            FailedAgentJobs = snapshot.FailedAgentJobs,
            GlobalTraceFlags = snapshot.GlobalTraceFlags,
        };
    }

    private static bool IsExcludedTable(
        TableInfo table,
        IReadOnlySet<string>? excludedSchemaSet,
        IReadOnlySet<string>? excludedTableSet)
    {
        return IsExcludedTable(table.SchemaName, table.TableName, excludedSchemaSet, excludedTableSet);
    }

    private static bool IsExcludedTable(
        string schemaName,
        string tableName,
        IReadOnlySet<string>? excludedSchemaSet,
        IReadOnlySet<string>? excludedTableSet)
    {
        return (excludedSchemaSet?.Contains(schemaName) == true)
               || (excludedTableSet is not null
                   && (excludedTableSet.Contains(tableName)
                       || excludedTableSet.Contains($"{schemaName}.{tableName}")));
    }

    private static async Task<ServerInfo> ReadServerInfoAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                CONVERT(sysname, @@SERVERNAME) AS server_name,
                DB_NAME() AS database_name,
                CONVERT(nvarchar(256), SERVERPROPERTY('Edition')) AS edition,
                CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS product_version,
                CONVERT(int, DATABASEPROPERTYEX(DB_NAME(), 'CompatibilityLevel')) AS compatibility_level,
                CASE WHEN CONVERT(int, DATABASEPROPERTYEX(DB_NAME(), 'IsAutoCreateStatistics')) = 1 THEN 1 ELSE 0 END AS auto_create_stats,
                CASE WHEN CONVERT(int, DATABASEPROPERTYEX(DB_NAME(), 'IsAutoUpdateStatistics')) = 1 THEN 1 ELSE 0 END AS auto_update_stats,
                CASE WHEN CONVERT(int, SERVERPROPERTY('EngineEdition')) IN (5, 8) THEN 1 ELSE 0 END AS is_azure
        """;

        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 30,
        };
        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Unable to read SQL Server metadata.");
        }

        return new ServerInfo(
            SqlRead.String(reader, "server_name"),
            SqlRead.String(reader, "database_name"),
            SqlRead.String(reader, "edition"),
            SqlRead.String(reader, "product_version"),
            SqlRead.Int(reader, "compatibility_level"),
            SqlRead.Bool(reader, "auto_create_stats"),
            SqlRead.Bool(reader, "auto_update_stats"),
            SqlRead.Bool(reader, "is_azure"));
    }

    private static async Task<IReadOnlyList<TableInfo>> ReadTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH table_pages AS
            (
                SELECT
                    p.object_id,
                    SUM(CASE WHEN p.index_id IN (0, 1) THEN p.rows ELSE 0 END) AS row_count,
                    SUM(a.total_pages) * 8.0 / 1024.0 AS reserved_mb
                FROM sys.partitions p
                INNER JOIN sys.allocation_units a ON a.container_id = p.partition_id
                GROUP BY p.object_id
            ),
            table_idx AS
            (
                SELECT
                    object_id,
                    MAX(CASE WHEN is_primary_key = 1 THEN 1 ELSE 0 END) AS has_primary_key,
                    MAX(CASE WHEN index_id = 0 THEN 1 ELSE 0 END) AS is_heap
                FROM sys.indexes
                GROUP BY object_id
            )
            SELECT
                t.object_id,
                s.name AS schema_name,
                t.name AS table_name,
                COALESCE(tp.row_count, 0) AS row_count,
                CONVERT(decimal(18,2), COALESCE(tp.reserved_mb, 0)) AS reserved_mb,
                COALESCE(ti.has_primary_key, 0) AS has_primary_key,
                COALESCE(ti.is_heap, 0) AS is_heap
            FROM sys.tables t
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            LEFT JOIN table_pages tp ON tp.object_id = t.object_id
            LEFT JOIN table_idx ti ON ti.object_id = t.object_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name
        """;

        return await ReadListAsync(connection, sql,
            reader => new TableInfo(
                SqlRead.Int(reader, "object_id"),
                SqlRead.String(reader, "schema_name"),
                SqlRead.String(reader, "table_name"),
                SqlRead.Long(reader, "row_count"),
                SqlRead.Decimal(reader, "reserved_mb"),
                SqlRead.Bool(reader, "has_primary_key"),
                SqlRead.Bool(reader, "is_heap")),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<IndexInfo>> ReadIndexesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                i.object_id,
                i.index_id,
                s.name AS schema_name,
                t.name AS table_name,
                i.name AS index_name,
                i.type_desc AS index_type,
                i.is_unique,
                i.is_primary_key,
                i.is_unique_constraint,
                i.is_disabled,
                i.is_hypothetical,
                i.fill_factor,
                COALESCE(k.key_columns, N'') AS key_columns,
                COALESCE(ic.included_columns, N'') AS included_columns,
                i.has_filter,
                i.filter_definition,
                COALESCE(k.key_size_bytes, 0) AS key_size_bytes,
                COALESCE(k.key_column_count, 0) AS key_column_count
            FROM sys.indexes i
            INNER JOIN sys.tables t ON t.object_id = i.object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            OUTER APPLY
            (
                SELECT
                    STUFF(
                        (
                            SELECT N',' + QUOTENAME(c.name)
                            FROM sys.index_columns ic2
                            INNER JOIN sys.columns c
                                ON c.object_id = ic2.object_id
                               AND c.column_id = ic2.column_id
                            WHERE ic2.object_id = i.object_id
                              AND ic2.index_id = i.index_id
                              AND ic2.is_included_column = 0
                            ORDER BY ic2.key_ordinal
                            FOR XML PATH(''), TYPE
                        ).value('.', 'nvarchar(max)'),
                        1,
                        1,
                        N''
                    ) AS key_columns,
                    SUM(CASE WHEN c.max_length = -1 THEN 8000 ELSE c.max_length END) AS key_size_bytes,
                    COUNT(*) AS key_column_count
                FROM sys.index_columns ic2
                INNER JOIN sys.columns c
                    ON c.object_id = ic2.object_id
                   AND c.column_id = ic2.column_id
                WHERE ic2.object_id = i.object_id
                  AND ic2.index_id = i.index_id
                  AND ic2.is_included_column = 0
            ) k
            OUTER APPLY
            (
                SELECT STUFF(
                    (
                        SELECT N',' + QUOTENAME(c.name)
                        FROM sys.index_columns ic3
                        INNER JOIN sys.columns c
                            ON c.object_id = ic3.object_id
                           AND c.column_id = ic3.column_id
                        WHERE ic3.object_id = i.object_id
                          AND ic3.index_id = i.index_id
                          AND ic3.is_included_column = 1
                        ORDER BY ic3.index_column_id
                        FOR XML PATH(''), TYPE
                    ).value('.', 'nvarchar(max)'),
                    1,
                    1,
                    N''
                ) AS included_columns
            ) ic
            WHERE t.is_ms_shipped = 0
              AND i.index_id > 0
            ORDER BY s.name, t.name, i.index_id
        """;

        return await ReadListAsync(connection, sql,
            reader => new IndexInfo(
                SqlRead.Int(reader, "object_id"),
                SqlRead.Int(reader, "index_id"),
                SqlRead.String(reader, "schema_name"),
                SqlRead.String(reader, "table_name"),
                SqlRead.String(reader, "index_name"),
                SqlRead.String(reader, "index_type"),
                SqlRead.Bool(reader, "is_unique"),
                SqlRead.Bool(reader, "is_primary_key"),
                SqlRead.Bool(reader, "is_unique_constraint"),
                SqlRead.Bool(reader, "is_disabled"),
                SqlRead.Bool(reader, "is_hypothetical"),
                SqlRead.Int(reader, "fill_factor"),
                SqlRead.String(reader, "key_columns"),
                SqlRead.String(reader, "included_columns"),
                SqlRead.Bool(reader, "has_filter"),
                SqlRead.NullableString(reader, "filter_definition"),
                SqlRead.Int(reader, "key_size_bytes"),
                SqlRead.Int(reader, "key_column_count")),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<IndexUsageInfo>> ReadIndexUsageAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                i.object_id,
                i.index_id,
                COALESCE(us.user_seeks, 0) AS user_seeks,
                COALESCE(us.user_scans, 0) AS user_scans,
                COALESCE(us.user_lookups, 0) AS user_lookups,
                COALESCE(us.user_updates, 0) AS user_updates,
                (
                    SELECT MAX(last_read)
                    FROM (VALUES (us.last_user_seek), (us.last_user_scan), (us.last_user_lookup)) reads(last_read)
                ) AS last_read_utc
            FROM sys.indexes i
            INNER JOIN sys.tables t ON t.object_id = i.object_id
            LEFT JOIN sys.dm_db_index_usage_stats us
                ON us.database_id = DB_ID()
               AND us.object_id = i.object_id
               AND us.index_id = i.index_id
            WHERE t.is_ms_shipped = 0
              AND i.index_id > 0
        """;

        return await ReadListAsync(connection, sql,
            reader => new IndexUsageInfo(
                SqlRead.Int(reader, "object_id"),
                SqlRead.Int(reader, "index_id"),
                SqlRead.Long(reader, "user_seeks"),
                SqlRead.Long(reader, "user_scans"),
                SqlRead.Long(reader, "user_lookups"),
                SqlRead.Long(reader, "user_updates"),
                SqlRead.NullableDateTimeOffset(reader, "last_read_utc")),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<IndexPhysicalInfo>> ReadIndexPhysicalStatsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                ps.object_id,
                ps.index_id,
                SUM(ps.page_count) AS page_count,
                AVG(CONVERT(float, ps.avg_fragmentation_in_percent)) AS fragmentation_percent,
                AVG(CONVERT(float, ps.avg_page_space_used_in_percent)) AS avg_page_space_used_percent
            FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ps
            INNER JOIN sys.indexes i
                ON i.object_id = ps.object_id
               AND i.index_id = ps.index_id
            INNER JOIN sys.tables t ON t.object_id = i.object_id
            WHERE t.is_ms_shipped = 0
              AND ps.index_id > 0
            GROUP BY ps.object_id, ps.index_id
        """;

        return await ReadListAsync(connection, sql,
            reader => new IndexPhysicalInfo(
                SqlRead.Int(reader, "object_id"),
                SqlRead.Int(reader, "index_id"),
                SqlRead.Long(reader, "page_count"),
                SqlRead.Double(reader, "fragmentation_percent"),
                SqlRead.Double(reader, "avg_page_space_used_percent")),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ForeignKeyInfo>> ReadForeignKeysAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                fk.object_id,
                fk.name AS fk_name,
                ps.name AS parent_schema,
                pt.name AS parent_table,
                rs.name AS referenced_schema,
                rt.name AS referenced_table,
                cols.parent_columns,
                cols.referenced_columns,
                cols.parent_types,
                cols.referenced_types,
                fk.is_disabled,
                fk.is_not_trusted,
                CASE WHEN EXISTS
                (
                    SELECT 1
                    FROM sys.indexes i
                    WHERE i.object_id = fk.parent_object_id
                      AND i.index_id > 0
                      AND i.is_hypothetical = 0
                      AND i.is_disabled = 0
                      AND NOT EXISTS
                      (
                          SELECT 1
                          FROM sys.foreign_key_columns fkc
                          LEFT JOIN sys.index_columns ic
                              ON ic.object_id = i.object_id
                             AND ic.index_id = i.index_id
                             AND ic.key_ordinal = fkc.constraint_column_id
                             AND ic.column_id = fkc.parent_column_id
                          WHERE fkc.constraint_object_id = fk.object_id
                            AND ic.index_column_id IS NULL
                      )
                ) THEN 1 ELSE 0 END AS has_supporting_index,
                fk.delete_referential_action_desc AS delete_action,
                fk.update_referential_action_desc AS update_action
            FROM sys.foreign_keys fk
            INNER JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
            INNER JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
            INNER JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
            INNER JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
            CROSS APPLY
            (
                SELECT
                    STUFF(
                        (
                            SELECT N',' + QUOTENAME(pc.name)
                            FROM sys.foreign_key_columns fkc2
                            INNER JOIN sys.columns pc
                                ON pc.object_id = fkc2.parent_object_id
                               AND pc.column_id = fkc2.parent_column_id
                            WHERE fkc2.constraint_object_id = fk.object_id
                            ORDER BY fkc2.constraint_column_id
                            FOR XML PATH(''), TYPE
                        ).value('.', 'nvarchar(max)'),
                        1,
                        1,
                        N''
                    ) AS parent_columns,
                    STUFF(
                        (
                            SELECT N',' + QUOTENAME(rc.name)
                            FROM sys.foreign_key_columns fkc2
                            INNER JOIN sys.columns rc
                                ON rc.object_id = fkc2.referenced_object_id
                               AND rc.column_id = fkc2.referenced_column_id
                            WHERE fkc2.constraint_object_id = fk.object_id
                            ORDER BY fkc2.constraint_column_id
                            FOR XML PATH(''), TYPE
                        ).value('.', 'nvarchar(max)'),
                        1,
                        1,
                        N''
                    ) AS referenced_columns,
                    STUFF(
                        (
                            SELECT N',' + CONCAT(ptype.name, N'(',
                                CASE
                                    WHEN ptype.name IN (N'nchar', N'nvarchar')
                                        THEN CASE WHEN pc.max_length = -1 THEN N'max' ELSE CONVERT(nvarchar(10), pc.max_length / 2) END
                                    WHEN ptype.name IN (N'char', N'varchar', N'binary', N'varbinary')
                                        THEN CASE WHEN pc.max_length = -1 THEN N'max' ELSE CONVERT(nvarchar(10), pc.max_length) END
                                    WHEN ptype.name IN (N'decimal', N'numeric')
                                        THEN CONCAT(CONVERT(nvarchar(10), pc.precision), N',', CONVERT(nvarchar(10), pc.scale))
                                    WHEN ptype.name IN (N'datetime2', N'datetimeoffset', N'time')
                                        THEN CONVERT(nvarchar(10), pc.scale)
                                    ELSE N''
                                END,
                            N')')
                            FROM sys.foreign_key_columns fkc2
                            INNER JOIN sys.columns pc
                                ON pc.object_id = fkc2.parent_object_id
                               AND pc.column_id = fkc2.parent_column_id
                            INNER JOIN sys.types ptype ON ptype.user_type_id = pc.user_type_id
                            WHERE fkc2.constraint_object_id = fk.object_id
                            ORDER BY fkc2.constraint_column_id
                            FOR XML PATH(''), TYPE
                        ).value('.', 'nvarchar(max)'),
                        1,
                        1,
                        N''
                    ) AS parent_types,
                    STUFF(
                        (
                            SELECT N',' + CONCAT(rtype.name, N'(',
                                CASE
                                    WHEN rtype.name IN (N'nchar', N'nvarchar')
                                        THEN CASE WHEN rc.max_length = -1 THEN N'max' ELSE CONVERT(nvarchar(10), rc.max_length / 2) END
                                    WHEN rtype.name IN (N'char', N'varchar', N'binary', N'varbinary')
                                        THEN CASE WHEN rc.max_length = -1 THEN N'max' ELSE CONVERT(nvarchar(10), rc.max_length) END
                                    WHEN rtype.name IN (N'decimal', N'numeric')
                                        THEN CONCAT(CONVERT(nvarchar(10), rc.precision), N',', CONVERT(nvarchar(10), rc.scale))
                                    WHEN rtype.name IN (N'datetime2', N'datetimeoffset', N'time')
                                        THEN CONVERT(nvarchar(10), rc.scale)
                                    ELSE N''
                                END,
                            N')')
                            FROM sys.foreign_key_columns fkc2
                            INNER JOIN sys.columns rc
                                ON rc.object_id = fkc2.referenced_object_id
                               AND rc.column_id = fkc2.referenced_column_id
                            INNER JOIN sys.types rtype ON rtype.user_type_id = rc.user_type_id
                            WHERE fkc2.constraint_object_id = fk.object_id
                            ORDER BY fkc2.constraint_column_id
                            FOR XML PATH(''), TYPE
                        ).value('.', 'nvarchar(max)'),
                        1,
                        1,
                        N''
                    ) AS referenced_types
            ) cols
            WHERE pt.is_ms_shipped = 0
              AND rt.is_ms_shipped = 0
        """;

        return await ReadListAsync(connection, sql,
            reader => new ForeignKeyInfo(
                SqlRead.Int(reader, "object_id"),
                SqlRead.String(reader, "fk_name"),
                SqlRead.String(reader, "parent_schema"),
                SqlRead.String(reader, "parent_table"),
                SqlRead.String(reader, "referenced_schema"),
                SqlRead.String(reader, "referenced_table"),
                SqlRead.String(reader, "parent_columns"),
                SqlRead.String(reader, "referenced_columns"),
                SqlRead.String(reader, "parent_types"),
                SqlRead.String(reader, "referenced_types"),
                SqlRead.Bool(reader, "is_disabled"),
                SqlRead.Bool(reader, "is_not_trusted"),
                SqlRead.Bool(reader, "has_supporting_index"),
                SqlRead.String(reader, "delete_action"),
                SqlRead.String(reader, "update_action")),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<StatisticsInfo>> ReadStatisticsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                st.object_id,
                st.stats_id,
                s.name AS schema_name,
                t.name AS table_name,
                st.name AS stats_name,
                st.auto_created,
                st.no_recompute,
                sp.last_updated,
                COALESCE(sp.rows, 0) AS rows_count,
                COALESCE(sp.modification_counter, 0) AS modification_counter
            FROM sys.stats st
            INNER JOIN sys.tables t ON t.object_id = st.object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            OUTER APPLY sys.dm_db_stats_properties(st.object_id, st.stats_id) sp
            WHERE t.is_ms_shipped = 0
        """;

        return await ReadListAsync(connection, sql,
            reader => new StatisticsInfo(
                SqlRead.Int(reader, "object_id"),
                SqlRead.Int(reader, "stats_id"),
                SqlRead.String(reader, "schema_name"),
                SqlRead.String(reader, "table_name"),
                SqlRead.String(reader, "stats_name"),
                SqlRead.Bool(reader, "auto_created"),
                SqlRead.Bool(reader, "no_recompute"),
                SqlRead.NullableDateTimeOffset(reader, "last_updated"),
                SqlRead.Long(reader, "rows_count"),
                SqlRead.Long(reader, "modification_counter")),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<IdentityColumnInfo>> ReadIdentityColumnsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                t.object_id,
                s.name AS schema_name,
                t.name AS table_name,
                c.name AS column_name,
                ty.name AS data_type,
                CONVERT(decimal(38,0), ic.last_value) AS last_value,
                CASE ty.name
                    WHEN N'tinyint' THEN CONVERT(decimal(38,0), 255)
                    WHEN N'smallint' THEN CONVERT(decimal(38,0), 32767)
                    WHEN N'int' THEN CONVERT(decimal(38,0), 2147483647)
                    WHEN N'bigint' THEN CONVERT(decimal(38,0), 9223372036854775807)
                    WHEN N'numeric' THEN POWER(CONVERT(decimal(38,0), 10), c.precision) - 1
                    WHEN N'decimal' THEN POWER(CONVERT(decimal(38,0), 10), c.precision) - 1
                    ELSE CONVERT(decimal(38,0), 0)
                END AS max_value,
                CASE
                    WHEN ic.last_value IS NULL THEN CONVERT(decimal(10,2), 0)
                    WHEN CASE ty.name
                        WHEN N'tinyint' THEN CONVERT(decimal(38,0), 255)
                        WHEN N'smallint' THEN CONVERT(decimal(38,0), 32767)
                        WHEN N'int' THEN CONVERT(decimal(38,0), 2147483647)
                        WHEN N'bigint' THEN CONVERT(decimal(38,0), 9223372036854775807)
                        WHEN N'numeric' THEN POWER(CONVERT(decimal(38,0), 10), c.precision) - 1
                        WHEN N'decimal' THEN POWER(CONVERT(decimal(38,0), 10), c.precision) - 1
                        ELSE CONVERT(decimal(38,0), 0)
                    END = 0 THEN CONVERT(decimal(10,2), 0)
                    ELSE CONVERT(decimal(10,2), (ABS(CONVERT(decimal(38,10), ic.last_value)) /
                        CASE ty.name
                            WHEN N'tinyint' THEN CONVERT(decimal(38,0), 255)
                            WHEN N'smallint' THEN CONVERT(decimal(38,0), 32767)
                            WHEN N'int' THEN CONVERT(decimal(38,0), 2147483647)
                            WHEN N'bigint' THEN CONVERT(decimal(38,0), 9223372036854775807)
                            WHEN N'numeric' THEN POWER(CONVERT(decimal(38,0), 10), c.precision) - 1
                            WHEN N'decimal' THEN POWER(CONVERT(decimal(38,0), 10), c.precision) - 1
                            ELSE CONVERT(decimal(38,0), 0)
                        END) * 100.0)
                END AS usage_percent
            FROM sys.identity_columns ic
            INNER JOIN sys.columns c
                ON c.object_id = ic.object_id
               AND c.column_id = ic.column_id
            INNER JOIN sys.tables t ON t.object_id = ic.object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE t.is_ms_shipped = 0
        """;

        return await ReadListAsync(connection, sql,
            reader => new IdentityColumnInfo(
                SqlRead.Int(reader, "object_id"),
                SqlRead.String(reader, "schema_name"),
                SqlRead.String(reader, "table_name"),
                SqlRead.String(reader, "column_name"),
                SqlRead.String(reader, "data_type"),
                SqlRead.NullableDecimal(reader, "last_value"),
                SqlRead.Decimal(reader, "max_value"),
                SqlRead.Decimal(reader, "usage_percent")),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ResourceIntensiveQueryInfo>> ReadTopResourceIntensiveQueriesAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (!await HasStateReadPermissionAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        const string sql = """
                SELECT TOP (10)
                    COALESCE(CONVERT(varchar(34), qs.query_hash, 1), N'0x0') AS query_hash,
                    qs.execution_count,
                    CONVERT(decimal(19,2), qs.total_worker_time / 1000.0) AS total_cpu_ms,
                    CONVERT(decimal(19,2), CASE WHEN qs.execution_count = 0 THEN 0 ELSE (qs.total_worker_time * 1.0 / qs.execution_count) / 1000.0 END) AS average_cpu_ms,
                    CONVERT(decimal(19,2), qs.total_elapsed_time / 1000.0) AS total_duration_ms,
                    CONVERT(decimal(19,2), CASE WHEN qs.execution_count = 0 THEN 0 ELSE (qs.total_elapsed_time * 1.0 / qs.execution_count) / 1000.0 END) AS average_duration_ms,
                    qs.total_logical_reads,
                    qs.total_logical_writes,
                    qs.last_execution_time AS last_execution_utc,
                    SUBSTRING(
                        st.text,
                        (qs.statement_start_offset / 2) + 1,
                        CASE
                            WHEN qs.statement_end_offset = -1 OR qs.statement_end_offset < qs.statement_start_offset
                                THEN (DATALENGTH(st.text) - qs.statement_start_offset) / 2 + 1
                            ELSE (qs.statement_end_offset - qs.statement_start_offset) / 2 + 1
                        END
                    ) AS query_text
                FROM sys.dm_exec_query_stats qs
                CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
                WHERE st.dbid = DB_ID()
                ORDER BY
                    qs.total_worker_time DESC,
                    qs.total_logical_reads DESC,
                    qs.execution_count DESC
            """;

        return await ReadListAsync(connection, sql,
            reader => new ResourceIntensiveQueryInfo(
                SqlRead.String(reader, "query_hash"),
                SqlRead.Long(reader, "execution_count"),
                SqlRead.Decimal(reader, "total_cpu_ms"),
                SqlRead.Decimal(reader, "average_cpu_ms"),
                SqlRead.Decimal(reader, "total_duration_ms"),
                SqlRead.Decimal(reader, "average_duration_ms"),
                SqlRead.Long(reader, "total_logical_reads"),
                SqlRead.Long(reader, "total_logical_writes"),
                SqlRead.NullableDateTimeOffset(reader, "last_execution_utc"),
                NormalizeQueryText(SqlRead.String(reader, "query_text"))),
            cancellationToken).ConfigureAwait(false);

    }

    private static async Task<IReadOnlyList<WaitStatInfo>> ReadTopWaitStatsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (!await HasStateReadPermissionAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        const string sql = """
                SELECT TOP (12)
                    ws.wait_type,
                    ws.waiting_tasks_count,
                    CONVERT(decimal(19,2), ws.wait_time_ms / 1000.0) AS wait_time_seconds,
                    CONVERT(decimal(19,2), (ws.wait_time_ms - ws.signal_wait_time_ms) / 1000.0) AS resource_wait_seconds,
                    CONVERT(decimal(19,2), ws.signal_wait_time_ms / 1000.0) AS signal_wait_seconds,
                    CONVERT(decimal(19,2), CASE WHEN ws.waiting_tasks_count = 0 THEN 0 ELSE ws.wait_time_ms * 1.0 / ws.waiting_tasks_count END) AS avg_wait_ms,
                    CASE
                        WHEN ws.wait_type LIKE 'LCK[_]%' THEN 'Locking'
                        WHEN ws.wait_type LIKE 'PAGEIOLATCH[_]%' OR ws.wait_type LIKE 'IO[_]%' THEN 'I/O'
                        WHEN ws.wait_type LIKE 'CX%' THEN 'Parallelism'
                        WHEN ws.wait_type LIKE 'RESOURCE[_]%' OR ws.wait_type LIKE 'MEMORY[_]%' THEN 'Memory'
                        WHEN ws.wait_type LIKE 'SOS[_]%' THEN 'CPU/Scheduler'
                        WHEN ws.wait_type LIKE 'ASYNC[_]NETWORK[_]IO' THEN 'Network'
                        ELSE 'Other'
                    END AS wait_category
                FROM sys.dm_os_wait_stats ws
                WHERE ws.waiting_tasks_count > 0
                  AND ws.wait_time_ms > 0
                  AND ws.wait_type NOT IN
                  (
                      N'BROKER_EVENTHANDLER', N'BROKER_RECEIVE_WAITFOR', N'BROKER_TASK_STOP',
                      N'BROKER_TO_FLUSH', N'BROKER_TRANSMITTER', N'CHECKPOINT_QUEUE',
                      N'CHKPT', N'CLR_AUTO_EVENT', N'CLR_MANUAL_EVENT', N'CLR_SEMAPHORE',
                      N'DBMIRROR_DBM_EVENT', N'DBMIRROR_EVENTS_QUEUE', N'DBMIRROR_WORKER_QUEUE',
                      N'DBMIRRORING_CMD', N'DIRTY_PAGE_POLL', N'DISPATCHER_QUEUE_SEMAPHORE',
                      N'EXECSYNC', N'FSAGENT', N'FT_IFTS_SCHEDULER_IDLE_WAIT', N'FT_IFTSHC_MUTEX',
                      N'HADR_CLUSAPI_CALL', N'HADR_FILESTREAM_IOMGR_IOCOMPLETION',
                      N'HADR_LOGCAPTURE_WAIT', N'HADR_NOTIFICATION_DEQUEUE',
                      N'HADR_TIMER_TASK', N'HADR_WORK_QUEUE', N'KSOURCE_WAKEUP',
                      N'LAZYWRITER_SLEEP', N'LOGMGR_QUEUE', N'MEMORY_ALLOCATION_EXT',
                      N'ONDEMAND_TASK_QUEUE', N'PREEMPTIVE_OS_FLUSHFILEBUFFERS',
                      N'PREEMPTIVE_XE_GETTARGETSTATE', N'PWAIT_ALL_COMPONENTS_INITIALIZED',
                      N'PWAIT_DIRECTLOGCONSUMER_GETNEXT', N'QDS_PERSIST_TASK_MAIN_LOOP_SLEEP',
                      N'QDS_ASYNC_QUEUE', N'QDS_CLEANUP_STALE_QUERIES_TASK_MAIN_LOOP_SLEEP',
                      N'QDS_SHUTDOWN_QUEUE', N'REQUEST_FOR_DEADLOCK_SEARCH', N'RESOURCE_QUEUE',
                      N'SERVER_IDLE_CHECK', N'SLEEP_BPOOL_FLUSH', N'SLEEP_DBSTARTUP',
                      N'SLEEP_DCOMSTARTUP', N'SLEEP_MASTERDBREADY', N'SLEEP_MASTERMDREADY',
                      N'SLEEP_MASTERUPGRADED', N'SLEEP_MSDBSTARTUP', N'SLEEP_SYSTEMTASK',
                      N'SLEEP_TASK', N'SLEEP_TEMPDBSTARTUP', N'SNI_HTTP_ACCEPT',
                      N'SP_SERVER_DIAGNOSTICS_SLEEP', N'SQLTRACE_BUFFER_FLUSH',
                      N'SQLTRACE_INCREMENTAL_FLUSH_SLEEP', N'SQLTRACE_WAIT_ENTRIES',
                      N'WAIT_FOR_RESULTS', N'WAITFOR', N'WAITFOR_TASKSHUTDOWN',
                      N'WAIT_XTP_HOST_WAIT', N'WAIT_XTP_OFFLINE_CKPT_NEW_LOG',
                      N'WAIT_XTP_CKPT_CLOSE', N'XE_DISPATCHER_JOIN', N'XE_DISPATCHER_WAIT',
                      N'XE_TIMER_EVENT'
                  )
                ORDER BY ws.wait_time_ms DESC
            """;

        return await ReadListAsync(connection, sql,
            reader => new WaitStatInfo(
                SqlRead.String(reader, "wait_type"),
                SqlRead.Long(reader, "waiting_tasks_count"),
                SqlRead.Decimal(reader, "wait_time_seconds"),
                SqlRead.Decimal(reader, "resource_wait_seconds"),
                SqlRead.Decimal(reader, "signal_wait_seconds"),
                SqlRead.Decimal(reader, "avg_wait_ms"),
                SqlRead.String(reader, "wait_category")),
            cancellationToken).ConfigureAwait(false);

    }

    private static async Task<IReadOnlyList<QueryStoreRegressionInfo>> ReadQueryStoreRegressionsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (!await HasStateReadPermissionAsync(connection, cancellationToken).ConfigureAwait(false)
    || !await IsQueryStoreEnabledAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        const string sql = """
                WITH runtime_data AS
                (
                    SELECT
                        q.query_id,
                        qt.query_sql_text,
                        rsi.start_time,
                        rs.count_executions,
                        rs.avg_duration
                    FROM sys.query_store_query q
                    INNER JOIN sys.query_store_query_text qt ON qt.query_text_id = q.query_text_id
                    INNER JOIN sys.query_store_plan p ON p.query_id = q.query_id
                    INNER JOIN sys.query_store_runtime_stats rs ON rs.plan_id = p.plan_id
                    INNER JOIN sys.query_store_runtime_stats_interval rsi ON rsi.runtime_stats_interval_id = rs.runtime_stats_interval_id
                ),
                baseline AS
                (
                    SELECT
                        query_id,
                        MAX(query_sql_text) AS query_sql_text,
                        SUM(count_executions) AS executions,
                        SUM(avg_duration * count_executions) / NULLIF(SUM(count_executions), 0) AS weighted_avg_duration
                    FROM runtime_data
                    WHERE start_time >= DATEADD(day, -14, SYSUTCDATETIME())
                      AND start_time < DATEADD(day, -3, SYSUTCDATETIME())
                    GROUP BY query_id
                ),
                recent AS
                (
                    SELECT
                        query_id,
                        SUM(count_executions) AS executions,
                        SUM(avg_duration * count_executions) / NULLIF(SUM(count_executions), 0) AS weighted_avg_duration,
                        MAX(start_time) AS last_interval_start
                    FROM runtime_data
                    WHERE start_time >= DATEADD(day, -3, SYSUTCDATETIME())
                    GROUP BY query_id
                )
                SELECT TOP (10)
                    b.query_id,
                    CONVERT(decimal(19,2), b.weighted_avg_duration / 1000.0) AS baseline_avg_ms,
                    CONVERT(decimal(19,2), r.weighted_avg_duration / 1000.0) AS recent_avg_ms,
                    CONVERT(decimal(19,2), r.weighted_avg_duration / NULLIF(b.weighted_avg_duration, 0)) AS regression_ratio,
                    r.executions AS recent_executions,
                    r.last_interval_start AS last_execution_utc,
                    b.query_sql_text
                FROM baseline b
                INNER JOIN recent r ON r.query_id = b.query_id
                WHERE b.executions >= 5
                  AND r.executions >= 5
                  AND r.weighted_avg_duration > b.weighted_avg_duration
                  AND r.weighted_avg_duration >= b.weighted_avg_duration * 1.5
                ORDER BY regression_ratio DESC, r.executions DESC
            """;

        return await ReadListAsync(connection, sql,
            reader => new QueryStoreRegressionInfo(
                SqlRead.Long(reader, "query_id"),
                SqlRead.Decimal(reader, "baseline_avg_ms"),
                SqlRead.Decimal(reader, "recent_avg_ms"),
                SqlRead.Decimal(reader, "regression_ratio"),
                SqlRead.Long(reader, "recent_executions"),
                SqlRead.NullableDateTimeOffset(reader, "last_execution_utc"),
                NormalizeQueryText(SqlRead.String(reader, "query_sql_text"))),
            cancellationToken).ConfigureAwait(false);

    }

    private static async Task<IReadOnlyList<BlockingSessionInfo>> ReadActiveBlockingSessionsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (!await HasStateReadPermissionAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        const string sql = """
                SELECT TOP (10)
                    r.blocking_session_id,
                    r.session_id AS blocked_session_id,
                    COALESCE(r.wait_type, N'') AS wait_type,
                    COALESCE(r.wait_time, 0) AS wait_time_ms,
                    COALESCE(r.wait_resource, N'') AS wait_resource,
                    SUBSTRING(
                        t.text,
                        (r.statement_start_offset / 2) + 1,
                        CASE
                            WHEN r.statement_end_offset = -1 OR r.statement_end_offset < r.statement_start_offset
                                THEN (DATALENGTH(t.text) - r.statement_start_offset) / 2 + 1
                            ELSE (r.statement_end_offset - r.statement_start_offset) / 2 + 1
                        END
                    ) AS query_text
                FROM sys.dm_exec_requests r
                CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
                WHERE r.blocking_session_id > 0
                  AND r.database_id = DB_ID()
                ORDER BY r.wait_time DESC
            """;

        return await ReadListAsync(connection, sql,
            reader => new BlockingSessionInfo(
                SqlRead.Int(reader, "blocking_session_id"),
                SqlRead.Int(reader, "blocked_session_id"),
                SqlRead.String(reader, "wait_type"),
                SqlRead.Long(reader, "wait_time_ms"),
                SqlRead.String(reader, "wait_resource"),
                NormalizeQueryText(SqlRead.String(reader, "query_text"))),
            cancellationToken).ConfigureAwait(false);

    }

    private static async Task<DeadlockSummaryInfo?> ReadDeadlockSummaryAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (!await HasStateReadPermissionAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        const string sql = """
                WITH deadlocks AS
                (
                    SELECT
                        TRY_CAST(event_data.value('(@timestamp)[1]', 'datetime2') AS datetime2) AS event_time
                    FROM
                    (
                        SELECT TRY_CAST(target_data AS xml) AS target_data
                        FROM sys.dm_xe_session_targets xt
                        INNER JOIN sys.dm_xe_sessions xs ON xs.address = xt.event_session_address
                        WHERE xs.name = N'system_health'
                          AND xt.target_name = N'ring_buffer'
                    ) src
                    CROSS APPLY src.target_data.nodes('/RingBufferTarget/event[@name="xml_deadlock_report"]') x(event_data)
                )
                SELECT
                    COUNT(*) AS deadlock_count,
                    MAX(event_time) AS last_deadlock_utc
                FROM deadlocks
                WHERE event_time >= DATEADD(day, -1, SYSUTCDATETIME())
            """;

        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 30,
        };

        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new DeadlockSummaryInfo(
            SqlRead.Long(reader, "deadlock_count"),
            SqlRead.NullableDateTimeOffset(reader, "last_deadlock_utc"));

    }

    private static async Task<IReadOnlyList<MissingIndexSignalInfo>> ReadMissingIndexSignalsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        if (!await HasStateReadPermissionAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        const string sql = """
                SELECT TOP (15)
                    mid.object_id,
                    s.name AS schema_name,
                    o.name AS table_name,
                    COALESCE(mid.equality_columns, N'') AS equality_columns,
                    COALESCE(mid.inequality_columns, N'') AS inequality_columns,
                    COALESCE(mid.included_columns, N'') AS included_columns,
                    migs.user_seeks,
                    migs.user_scans,
                    CONVERT(decimal(19,2), migs.avg_total_user_cost) AS avg_total_user_cost,
                    CONVERT(decimal(19,2), migs.avg_user_impact) AS avg_user_impact,
                    CONVERT(decimal(19,2), migs.avg_total_user_cost * (migs.avg_user_impact / 100.0) * (migs.user_seeks + migs.user_scans)) AS estimated_benefit,
                    COALESCE(idx.existing_index_count, 0) AS existing_index_count
                FROM sys.dm_db_missing_index_group_stats migs
                INNER JOIN sys.dm_db_missing_index_groups mig ON mig.index_group_handle = migs.group_handle
                INNER JOIN sys.dm_db_missing_index_details mid ON mid.index_handle = mig.index_handle
                INNER JOIN sys.objects o ON o.object_id = mid.object_id
                INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
                OUTER APPLY
                (
                    SELECT COUNT(*) AS existing_index_count
                    FROM sys.indexes i
                    WHERE i.object_id = mid.object_id
                      AND i.index_id > 0
                      AND i.is_hypothetical = 0
                ) idx
                WHERE mid.database_id = DB_ID()
                  AND o.type = 'U'
                  AND (migs.user_seeks + migs.user_scans) >= 100
                  AND migs.avg_user_impact >= 70
                ORDER BY estimated_benefit DESC
            """;

        return await ReadListAsync(connection, sql,
            reader =>
            {
                var existingIndexCount = SqlRead.Int(reader, "existing_index_count");
                var guardrailNote = existingIndexCount >= 12
                    ? "High existing index count; validate carefully before adding more indexes."
                    : "Signal passes read-benefit guardrails.";

                return new MissingIndexSignalInfo(
                    SqlRead.Int(reader, "object_id"),
                    SqlRead.String(reader, "schema_name"),
                    SqlRead.String(reader, "table_name"),
                    SqlRead.String(reader, "equality_columns"),
                    SqlRead.String(reader, "inequality_columns"),
                    SqlRead.String(reader, "included_columns"),
                    SqlRead.Long(reader, "user_seeks"),
                    SqlRead.Long(reader, "user_scans"),
                    SqlRead.Decimal(reader, "avg_total_user_cost"),
                    SqlRead.Decimal(reader, "avg_user_impact"),
                    SqlRead.Decimal(reader, "estimated_benefit"),
                    existingIndexCount,
                    guardrailNote);
            },
            cancellationToken).ConfigureAwait(false);

    }

    private static async Task<LogHealthInfo?> ReadLogHealthAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
                SELECT
                    CONVERT(decimal(19,2), ls.total_log_size_mb) AS total_log_size_mb,
                    CONVERT(decimal(19,2), ls.used_log_space_mb) AS used_log_size_mb,
                    CONVERT(decimal(19,2), ls.used_log_space_in_percent) AS used_log_percent,
                    COALESCE(vlf.vlf_count, 0) AS vlf_count,
                    COALESCE(tx.longest_active_tx_minutes, 0) AS longest_active_tx_minutes,
                    COALESCE(d.log_reuse_wait_desc, N'UNKNOWN') AS log_reuse_wait_desc
                FROM sys.databases d
                CROSS APPLY sys.dm_db_log_space_usage ls
                OUTER APPLY
                (
                    SELECT COUNT(*) AS vlf_count
                    FROM sys.dm_db_log_info(DB_ID())
                ) vlf
                OUTER APPLY
                (
                    SELECT MAX(DATEDIFF(minute, at.transaction_begin_time, SYSUTCDATETIME())) AS longest_active_tx_minutes
                    FROM sys.dm_tran_database_transactions dt
                    INNER JOIN sys.dm_tran_active_transactions at ON at.transaction_id = dt.transaction_id
                    WHERE dt.database_id = DB_ID()
                ) tx
                WHERE d.database_id = DB_ID()
            """;

        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 30,
        };

        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new LogHealthInfo(
            SqlRead.Decimal(reader, "total_log_size_mb"),
            SqlRead.Decimal(reader, "used_log_size_mb"),
            SqlRead.Decimal(reader, "used_log_percent"),
            SqlRead.Int(reader, "vlf_count"),
            SqlRead.Long(reader, "longest_active_tx_minutes"),
            SqlRead.String(reader, "log_reuse_wait_desc"));

    }

    private static async Task<TempDbPressureInfo?> ReadTempDbPressureAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
                SELECT
                    CONVERT(decimal(19,2), SUM(version_store_reserved_page_count) * 8.0 / 1024.0) AS version_store_mb,
                    CONVERT(decimal(19,2), SUM(user_object_reserved_page_count) * 8.0 / 1024.0) AS user_object_mb,
                    CONVERT(decimal(19,2), SUM(internal_object_reserved_page_count) * 8.0 / 1024.0) AS internal_object_mb,
                    CONVERT(decimal(19,2), SUM(unallocated_extent_page_count) * 8.0 / 1024.0) AS unallocated_mb
                FROM tempdb.sys.dm_db_file_space_usage
            """;

        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 30,
        };

        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new TempDbPressureInfo(
            SqlRead.Decimal(reader, "version_store_mb"),
            SqlRead.Decimal(reader, "user_object_mb"),
            SqlRead.Decimal(reader, "internal_object_mb"),
            SqlRead.Decimal(reader, "unallocated_mb"));

    }

    private static async Task<IReadOnlyList<FileGrowthHealthInfo>> ReadFileGrowthHealthAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
                SELECT
                    df.file_id,
                    df.name AS logical_name,
                    df.type_desc AS file_type,
                    df.physical_name,
                    CONVERT(decimal(19,2), df.size * 8.0 / 1024.0) AS size_mb,
                    df.is_percent_growth,
                    CONVERT(decimal(19,2), CASE
                        WHEN df.is_percent_growth = 1 THEN df.growth
                        ELSE df.growth * 8.0 / 1024.0
                    END) AS growth_value,
                    CONVERT(decimal(19,2), CASE
                        WHEN df.max_size = -1 THEN NULL
                        ELSE df.max_size * 8.0 / 1024.0
                    END) AS max_size_mb
                FROM sys.database_files df
                ORDER BY df.type, df.file_id
            """;

        return await ReadListAsync(connection, sql,
            reader =>
            {
                var isPercentGrowth = SqlRead.Bool(reader, "is_percent_growth");
                var growthValue = SqlRead.Decimal(reader, "growth_value");
                var growthDescription = isPercentGrowth
                    ? $"{growthValue.ToString("0.##", CultureInfo.InvariantCulture)}%"
                    : $"{growthValue.ToString("0.##", CultureInfo.InvariantCulture)} MB";

                var advisory = "Growth setting looks reasonable.";
                if (isPercentGrowth)
                {
                    advisory = "Percent growth can create uneven file growth and long autogrowth events.";
                }
                else if (growthValue < 64m)
                {
                    advisory = "Growth increment below 64 MB may cause frequent autogrowth operations.";
                }

                return new FileGrowthHealthInfo(
                    SqlRead.Int(reader, "file_id"),
                    SqlRead.String(reader, "logical_name"),
                    SqlRead.String(reader, "file_type"),
                    SqlRead.String(reader, "physical_name"),
                    SqlRead.Decimal(reader, "size_mb"),
                    isPercentGrowth,
                    growthValue,
                    SqlRead.NullableDecimal(reader, "max_size_mb"),
                    growthDescription,
                    advisory);
            },
            cancellationToken).ConfigureAwait(false);

    }

    private static async Task<BackupPostureInfo?> ReadBackupPostureAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
                SELECT
                    d.recovery_model_desc,
                    MAX(CASE WHEN b.type = 'D' THEN b.backup_finish_date END) AS last_full_backup_utc,
                    MAX(CASE WHEN b.type = 'I' THEN b.backup_finish_date END) AS last_diff_backup_utc,
                    MAX(CASE WHEN b.type = 'L' THEN b.backup_finish_date END) AS last_log_backup_utc
                FROM sys.databases d
                LEFT JOIN msdb.dbo.backupset b ON b.database_name = d.name
                WHERE d.database_id = DB_ID()
                GROUP BY d.recovery_model_desc
            """;

        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 30,
        };

        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var lastFull = SqlRead.NullableDateTimeOffset(reader, "last_full_backup_utc");
        var lastDiff = SqlRead.NullableDateTimeOffset(reader, "last_diff_backup_utc");
        var lastLog = SqlRead.NullableDateTimeOffset(reader, "last_log_backup_utc");

        return new BackupPostureInfo(
            SqlRead.String(reader, "recovery_model_desc"),
            lastFull,
            lastDiff,
            lastLog,
            lastFull is null ? null : Convert.ToDecimal((now - lastFull.Value).TotalHours, CultureInfo.InvariantCulture),
            lastDiff is null ? null : Convert.ToDecimal((now - lastDiff.Value).TotalHours, CultureInfo.InvariantCulture),
            lastLog is null ? null : Convert.ToDecimal((now - lastLog.Value).TotalHours, CultureInfo.InvariantCulture));

    }

    private static async Task<IReadOnlyList<ColumnInfo>> ReadColumnsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                c.object_id,
                s.name AS schema_name,
                t.name AS table_name,
                c.name AS column_name,
                tp.name AS data_type,
                c.max_length,
                c.precision,
                c.scale,
                c.is_nullable,
                c.column_id
            FROM sys.columns c
            INNER JOIN sys.tables t ON t.object_id = c.object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            INNER JOIN sys.types tp ON tp.user_type_id = c.user_type_id
            WHERE t.is_ms_shipped = 0
            ORDER BY s.name, t.name, c.column_id
        """;

        return await ReadListAsync(connection, sql,
            reader => new ColumnInfo(
                SqlRead.Int(reader, "object_id"),
                SqlRead.String(reader, "schema_name"),
                SqlRead.String(reader, "table_name"),
                SqlRead.String(reader, "column_name"),
                SqlRead.String(reader, "data_type"),
                SqlRead.Int(reader, "max_length"),
                SqlRead.Int(reader, "precision"),
                SqlRead.Int(reader, "scale"),
                SqlRead.Bool(reader, "is_nullable"),
                SqlRead.Int(reader, "column_id")),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ColumnNullStats>> ReadColumnNullStatsAsync(
        SqlConnection connection,
        IReadOnlyList<ColumnInfo> columns,
        IReadOnlyList<TableInfo> tables,
        CancellationToken cancellationToken)
    {
        const int maxColumnsPerTable = 30;
        const long maxRowsForSampling = 5_000_000;
        const int minRowsForSampling = 1_000;

        var rowCountByObjectId = tables.ToDictionary(t => t.ObjectId, t => t.RowCount);

        var nullableByTable = columns
            .Where(c => c.IsNullable && rowCountByObjectId.TryGetValue(c.ObjectId, out var rows)
                && rows >= minRowsForSampling && rows <= maxRowsForSampling)
            .GroupBy(c => c.ObjectId);

        var results = new List<ColumnNullStats>();

        foreach (var group in nullableByTable)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cols = group.Take(maxColumnsPerTable).ToArray();
            var first = cols[0];
            var qualifiedTable = $"[{EscapeBracket(first.SchemaName)}].[{EscapeBracket(first.TableName)}]";

            var parts = cols.Select(c =>
                $"SELECT N'{EscapeSqlString(c.ColumnName)}' AS column_name, " +
                $"CASE WHEN EXISTS(SELECT 1 FROM {qualifiedTable} WITH (NOLOCK) WHERE [{EscapeBracket(c.ColumnName)}] IS NULL) THEN 1 ELSE 0 END AS has_nulls");

            var sql = $"SELECT column_name FROM ({string.Join(" UNION ALL ", parts)}) x WHERE has_nulls = 0";

            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var colName = reader.GetString(0);
                var columnInfo = cols.FirstOrDefault(c => string.Equals(c.ColumnName, colName, StringComparison.Ordinal));
                if (columnInfo is null)
                {
                    continue;
                }

                results.Add(new ColumnNullStats(
                    first.ObjectId,
                    first.SchemaName,
                    first.TableName,
                    colName,
                    FormatColumnDataType(columnInfo)));
            }
        }

        return results;
    }

    private static string EscapeBracket(string name) => name.Replace("]", "]]", StringComparison.Ordinal);

    private static string FormatColumnDataType(ColumnInfo column)
    {
        var dataType = column.DataType;
        var type = dataType.ToLowerInvariant();

        if (type is "varchar" or "char" or "varbinary" or "binary")
        {
            var length = column.MaxLength == -1 ? "max" : column.MaxLength.ToString(CultureInfo.InvariantCulture);
            return $"{dataType}({length})";
        }

        if (type is "nvarchar" or "nchar")
        {
            var length = column.MaxLength == -1
                ? "max"
                : (column.MaxLength / 2).ToString(CultureInfo.InvariantCulture);
            return $"{dataType}({length})";
        }

        if (type is "decimal" or "numeric")
        {
            return $"{dataType}({column.Precision.ToString(CultureInfo.InvariantCulture)},{column.Scale.ToString(CultureInfo.InvariantCulture)})";
        }

        if (type is "datetime2" or "datetimeoffset" or "time")
        {
            return $"{dataType}({column.Scale.ToString(CultureInfo.InvariantCulture)})";
        }

        return dataType;
    }

    private static string EscapeSqlString(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static async Task<IReadOnlyList<SecurityHygieneIssueInfo>> ReadSecurityHygieneIssuesAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
                SELECT TOP (50)
                    issue_type,
                    severity,
                    principal_name,
                    details
                FROM
                (
                    SELECT
                        N'OrphanUser' AS issue_type,
                        N'High' AS severity,
                        dp.name AS principal_name,
                        CONCAT(N'Principal exists in database but not at server scope (', dp.type_desc, N').') AS details,
                        1 AS sort_order
                    FROM sys.database_principals dp
                    LEFT JOIN sys.server_principals sp ON dp.sid = sp.sid
                    WHERE dp.type IN ('S', 'U', 'G')
                      AND dp.name NOT IN (N'dbo', N'guest', N'INFORMATION_SCHEMA', N'sys')
                      AND dp.sid IS NOT NULL
                      AND sp.sid IS NULL

                    UNION ALL

                    SELECT
                        N'DbOwnerMembership',
                        N'Medium',
                        member_principal.name,
                        N'Principal is member of db_owner role.',
                        2
                    FROM sys.database_role_members drm
                    INNER JOIN sys.database_principals role_principal ON role_principal.principal_id = drm.role_principal_id
                    INNER JOIN sys.database_principals member_principal ON member_principal.principal_id = drm.member_principal_id
                    WHERE role_principal.name = N'db_owner'
                      AND member_principal.name <> N'dbo'

                    UNION ALL

                    SELECT
                        N'PublicGrant',
                        N'Medium',
                        N'public',
                        CONCAT(
                            N'Public has ', perm.permission_name, N' on ',
                            CASE
                                WHEN perm.major_id > 0 THEN QUOTENAME(OBJECT_SCHEMA_NAME(perm.major_id)) + N'.' + QUOTENAME(OBJECT_NAME(perm.major_id))
                                ELSE perm.class_desc
                            END),
                        3
                    FROM sys.database_permissions perm
                    INNER JOIN sys.database_principals grantee ON grantee.principal_id = perm.grantee_principal_id
                    WHERE grantee.name = N'public'
                      AND perm.state IN ('G', 'W')
                      AND perm.permission_name IN (N'ALTER', N'CONTROL', N'VIEW DEFINITION', N'EXECUTE')
                ) issues
                ORDER BY sort_order, principal_name
            """;

        return await ReadListAsync(connection, sql,
            reader => new SecurityHygieneIssueInfo(
                SqlRead.String(reader, "issue_type"),
                ParseSeverity(SqlRead.String(reader, "severity")),
                SqlRead.String(reader, "principal_name"),
                SqlRead.String(reader, "details")),
            cancellationToken).ConfigureAwait(false);

    }

    private static async Task<bool> IsQueryStoreEnabledAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CASE
                       WHEN EXISTS
                       (
                           SELECT 1
                           FROM sys.database_query_store_options
                           WHERE actual_state_desc IN (N'READ_WRITE', N'READ_ONLY')
                       ) THEN 1
                       ELSE 0
                   END
        """;

        return await ExecuteBoolScalarAsync(connection, sql, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> HasStateReadPermissionAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT CASE
                       WHEN HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DATABASE STATE') = 1
                            OR HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW SERVER STATE') = 1
                           THEN 1
                       ELSE 0
                   END
        """;

        return await ExecuteBoolScalarAsync(connection, sql, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ExecuteBoolScalarAsync(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 30,
        };

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null || result is DBNull)
        {
            return false;
        }

        return result switch
        {
            bool value => value,
            byte value => value == 1,
            short value => value == 1,
            int value => value == 1,
            long value => value == 1,
            _ => Convert.ToBoolean(result, CultureInfo.InvariantCulture),
        };
    }

    private static async Task<IReadOnlyList<T>> TryReadOptionalListAsync<T>(
        Func<Task<IReadOnlyList<T>>> read,
        List<CollectionWarning> warnings,
        string section)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            warnings.Add(new CollectionWarning(section, ex.Message));
            return [];
        }
    }

    private static async Task<T?> TryReadOptionalAsync<T>(
        Func<Task<T?>> read,
        List<CollectionWarning> warnings,
        string section)
        where T : class
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            warnings.Add(new CollectionWarning(section, ex.Message));
            return null;
        }
    }

    private static async Task<T?> TryReadOptionalStructAsync<T>(
        Func<Task<T?>> read,
        List<CollectionWarning> warnings,
        string section)
        where T : struct
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (SqlException ex)
        {
            warnings.Add(new CollectionWarning(section, ex.Message));
            return null;
        }
    }

    private static AuditSeverity ParseSeverity(string value)
    {
        return Enum.TryParse<AuditSeverity>(value, ignoreCase: true, out var severity)
            ? severity
            : AuditSeverity.Info;
    }

    private static string NormalizeQueryText(string queryText)
    {
        const int maxLength = 300;

        var normalized = System.Text.RegularExpressions.Regex.Replace(queryText, @"\s+", " ", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100)).Trim();

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return $"{normalized.AsSpan(0, maxLength)}...";
    }

    private static async Task<IReadOnlyList<T>> ReadListAsync<T>(
        SqlConnection connection,
        string sql,
        Func<SqlDataReader, T> map,
        CancellationToken cancellationToken)
    {
        var rows = new List<T>();

        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 120,
        };

        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    private sealed record ServerInfo(
        string ServerName,
        string DatabaseName,
        string Edition,
        string ProductVersion,
        int CompatibilityLevel,
        bool AutoCreateStatisticsOn,
        bool AutoUpdateStatisticsOn,
        bool IsAzureSql);

    private static async Task<IReadOnlyList<ServerConfigInfo>> ReadServerConfigurationsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT name, value_in_use, description
            FROM sys.configurations
            ORDER BY name
            """;
        return await ReadListAsync(
            connection,
            sql,
            r => new ServerConfigInfo(
                SqlRead.String(r, "name"),
                SqlRead.Decimal(r, "value_in_use"),
                SqlRead.String(r, "description")),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DateTimeOffset?> ReadLastDbccCheckDbAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DBCC DBINFO() WITH TABLERESULTS, NO_INFOMSGS
            """;
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 60,
        };
        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var field = SqlRead.String(reader, "Field");
            if (field.Equals("dbi_dbccLastKnownGood", StringComparison.OrdinalIgnoreCase))
            {
                var val = SqlRead.String(reader, "Value");
                if (DateTimeOffset.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
                    && dt.Year > 1900)
                {
                    return dt;
                }

                return null;
            }
        }

        return null;
    }

    private static async Task<TempDbConfigInfo?> ReadTempDbConfigAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string filesSql = """
            SELECT size * 8.0 / 1024 AS size_mb
            FROM tempdb.sys.database_files
            WHERE type = 0
            """;
        var fileSizes = await ReadListAsync(
            connection,
            filesSql,
            r => SqlRead.Decimal(r, "size_mb"),
            cancellationToken).ConfigureAwait(false);

        int logicalCpuCount;
        try
        {
            const string cpuSql = "SELECT cpu_count FROM sys.dm_os_sys_info";
            await using var cmd = new SqlCommand(cpuSql, connection)
            {
                CommandTimeout = 30,
            };
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            logicalCpuCount = result is DBNull || result is null ? 0 : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (SqlException)
        {
            logicalCpuCount = 0;
        }

        return new TempDbConfigInfo(fileSizes.Count, logicalCpuCount, fileSizes);
    }

    private static async Task<IReadOnlyList<SleepingTransactionInfo>> ReadSleepingTransactionsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                s.session_id,
                s.login_name,
                DB_NAME(s.database_id) AS database_name,
                s.open_transaction_count,
                s.total_elapsed_time / 60000.0 AS elapsed_minutes,
                ISNULL(CAST(t.text AS NVARCHAR(300)), N'') AS last_query_text
            FROM sys.dm_exec_sessions AS s
            LEFT JOIN sys.dm_exec_connections AS c ON s.session_id = c.session_id
            OUTER APPLY sys.dm_exec_sql_text(c.most_recent_sql_handle) AS t
            WHERE s.status = 'sleeping'
              AND s.open_transaction_count > 0
              AND s.session_id <> @@SPID
            ORDER BY s.total_elapsed_time DESC
            """;
        return await ReadListAsync(
            connection,
            sql,
            r => new SleepingTransactionInfo(
                SqlRead.Int(r, "session_id"),
                SqlRead.String(r, "login_name"),
                SqlRead.String(r, "database_name"),
                SqlRead.Int(r, "open_transaction_count"),
                SqlRead.Decimal(r, "elapsed_minutes"),
                SqlRead.String(r, "last_query_text")),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<MemoryPressureInfo?> ReadMemoryPressureAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT object_name, counter_name, cntr_value
            FROM sys.dm_os_performance_counters
            WHERE (object_name LIKE '%Buffer Manager%'
                   AND counter_name IN ('Page life expectancy', 'Buffer cache hit ratio', 'Buffer cache hit ratio base'))
               OR (object_name LIKE '%Memory Manager%'
                   AND counter_name IN ('Total Server Memory (KB)', 'Target Server Memory (KB)'))
            """;
        var counters = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 30,
        };
        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var counterName = SqlRead.String(reader, "counter_name").Trim();
            var value = SqlRead.Long(reader, "cntr_value");
            counters[counterName] = value;
        }

        if (!counters.TryGetValue("Page life expectancy", out var ple))
        {
            return null;
        }

        counters.TryGetValue("Buffer cache hit ratio", out var hitRatioRaw);
        counters.TryGetValue("Buffer cache hit ratio base", out var hitRatioBase);
        counters.TryGetValue("Total Server Memory (KB)", out var totalKb);
        counters.TryGetValue("Target Server Memory (KB)", out var targetKb);

        var hitRatio = hitRatioBase > 0
            ? Math.Round((decimal)hitRatioRaw / hitRatioBase * 100, 1)
            : 0m;

        return new MemoryPressureInfo(
            ple,
            hitRatio,
            Math.Round(totalKb / 1024m, 1),
            Math.Round(targetKb / 1024m, 1));
    }

    private static async Task<IReadOnlyList<FileIoLatencyInfo>> ReadFileIoLatencyAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                f.database_id,
                f.file_id,
                mf.name AS logical_name,
                CASE mf.type WHEN 0 THEN 'ROWS' WHEN 1 THEN 'LOG' ELSE 'OTHER' END AS file_type,
                f.num_of_reads,
                f.num_of_writes,
                CASE WHEN f.num_of_reads = 0 THEN 0
                     ELSE CAST(f.io_stall_read_ms / f.num_of_reads AS DECIMAL(10,2)) END AS avg_read_latency_ms,
                CASE WHEN f.num_of_writes = 0 THEN 0
                     ELSE CAST(f.io_stall_write_ms / f.num_of_writes AS DECIMAL(10,2)) END AS avg_write_latency_ms,
                mf.size * 8.0 / 1024 AS size_mb
            FROM sys.dm_io_virtual_file_stats(NULL, NULL) AS f
            INNER JOIN sys.master_files AS mf
                ON f.database_id = mf.database_id AND f.file_id = mf.file_id
            ORDER BY f.database_id, f.file_id
            """;
        return await ReadListAsync(
            connection,
            sql,
            r => new FileIoLatencyInfo(
                SqlRead.Int(r, "database_id"),
                SqlRead.Int(r, "file_id"),
                SqlRead.String(r, "logical_name"),
                SqlRead.String(r, "file_type"),
                SqlRead.Long(r, "num_of_reads"),
                SqlRead.Long(r, "num_of_writes"),
                SqlRead.Decimal(r, "avg_read_latency_ms"),
                SqlRead.Decimal(r, "avg_write_latency_ms"),
                SqlRead.Decimal(r, "size_mb")),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PlanCacheInfo?> ReadPlanCacheAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                COUNT(1) AS total_plans,
                SUM(CASE WHEN usecounts = 1 THEN 1 ELSE 0 END) AS single_use_plans,
                CAST(SUM(CASE WHEN usecounts = 1 THEN 1 ELSE 0 END) * 100.0
                     / NULLIF(COUNT(1), 0) AS DECIMAL(5,1)) AS single_use_pct,
                CAST(SUM(size_in_bytes) / 1048576.0 AS DECIMAL(10,1)) AS cache_size_mb,
                CAST(SUM(CASE WHEN objtype = 'Adhoc' THEN size_in_bytes ELSE 0 END) / 1048576.0
                     AS DECIMAL(10,1)) AS adhoc_cache_size_mb
            FROM sys.dm_exec_cached_plans
            """;
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 60,
        };
        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new PlanCacheInfo(
                SqlRead.Int(reader, "total_plans"),
                SqlRead.Int(reader, "single_use_plans"),
                SqlRead.Decimal(reader, "single_use_pct"),
                SqlRead.Decimal(reader, "cache_size_mb"),
                SqlRead.Decimal(reader, "adhoc_cache_size_mb"));
        }

        return null;
    }

    private static async Task<IReadOnlyList<TableCompressionInfo>> ReadTableCompressionAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                t.object_id,
                s.name AS schema_name,
                t.name AS table_name,
                p.partition_number,
                p.data_compression_desc,
                p.rows,
                ps.used_page_count
            FROM sys.tables AS t
            INNER JOIN sys.schemas AS s ON t.schema_id = s.schema_id
            INNER JOIN sys.partitions AS p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
            INNER JOIN sys.dm_db_partition_stats AS ps
                ON p.object_id = ps.object_id AND p.partition_number = ps.partition_number AND ps.index_id IN (0, 1)
            ORDER BY ps.used_page_count DESC
            """;
        return await ReadListAsync(
            connection,
            sql,
            r => new TableCompressionInfo(
                SqlRead.Int(r, "object_id"),
                SqlRead.String(r, "schema_name"),
                SqlRead.String(r, "table_name"),
                SqlRead.Int(r, "partition_number"),
                SqlRead.String(r, "data_compression_desc"),
                SqlRead.Long(r, "rows"),
                SqlRead.Long(r, "used_page_count")),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<DatabaseOptionsInfo?> ReadDatabaseOptionsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                is_auto_shrink_on,
                is_auto_close_on,
                page_verify_option_desc,
                is_read_committed_snapshot_on,
                is_query_store_on,
                COALESCE(query_store_state_desc, N'OFF') AS query_store_state_desc
            FROM sys.databases
            WHERE database_id = DB_ID()
            """;
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 30,
        };
        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new DatabaseOptionsInfo(
                SqlRead.Bool(reader, "is_auto_shrink_on"),
                SqlRead.Bool(reader, "is_auto_close_on"),
                SqlRead.String(reader, "page_verify_option_desc"),
                SqlRead.Bool(reader, "is_read_committed_snapshot_on"),
                SqlRead.Bool(reader, "is_query_store_on"),
                SqlRead.String(reader, "query_store_state_desc"));
        }

        return null;
    }

    private static async Task<IReadOnlyList<VolumeInfo>> ReadVolumeStatsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                vs.volume_mount_point,
                vs.total_bytes,
                vs.available_bytes,
                CAST(vs.available_bytes * 100.0 / NULLIF(vs.total_bytes, 0) AS DECIMAL(5,1)) AS available_pct,
                mf.name AS logical_name,
                CASE mf.type WHEN 0 THEN 'ROWS' WHEN 1 THEN 'LOG' ELSE 'OTHER' END AS file_type
            FROM sys.database_files AS mf
            CROSS APPLY sys.dm_os_volume_stats(DB_ID(), mf.file_id) AS vs
            """;
        return await ReadListAsync(
            connection,
            sql,
            r => new VolumeInfo(
                SqlRead.String(r, "volume_mount_point"),
                SqlRead.Long(r, "total_bytes"),
                SqlRead.Long(r, "available_bytes"),
                SqlRead.Decimal(r, "available_pct"),
                SqlRead.String(r, "logical_name"),
                SqlRead.String(r, "file_type")),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<FailedAgentJobInfo>> ReadFailedAgentJobsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP 50
                j.name AS job_name,
                js.step_name,
                CONVERT(datetime,
                    CONVERT(char(8), h.run_date, 112) + ' '
                    + STUFF(STUFF(RIGHT('000000' + CONVERT(varchar, h.run_time), 6), 5, 0, ':'), 3, 0, ':')
                ) AS last_run_datetime,
                ISNULL(NULLIF(h.message, ''), 'No error message recorded') AS error_message,
                h.run_duration
            FROM msdb.dbo.sysjobhistory AS h
            INNER JOIN msdb.dbo.sysjobs AS j ON j.job_id = h.job_id
            INNER JOIN msdb.dbo.sysjobsteps AS js ON js.job_id = h.job_id AND js.step_id = h.step_id
            WHERE h.run_status = 0
              AND h.run_date >= CONVERT(int, CONVERT(char(8), DATEADD(day, -7, GETDATE()), 112))
            ORDER BY h.run_date DESC, h.run_time DESC
            """;
        return await ReadListAsync(
            connection,
            sql,
            r =>
            {
                var runDt = SqlRead.NullableDateTimeOffset(r, "last_run_datetime")
                    ?? DateTimeOffset.MinValue;
                var rawDuration = SqlRead.Int(r, "run_duration");
                var hours = rawDuration / 10000;
                var minutes = (rawDuration % 10000) / 100;
                var seconds = rawDuration % 100;
                return new FailedAgentJobInfo(
                    SqlRead.String(r, "job_name"),
                    SqlRead.String(r, "step_name"),
                    runDt,
                    SqlRead.String(r, "error_message"),
                    (hours * 3600) + (minutes * 60) + seconds);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<GlobalTraceFlagInfo>> ReadGlobalTraceFlagsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = "DBCC TRACESTATUS(-1) WITH NO_INFOMSGS";
        return await ReadListAsync(
            connection,
            sql,
            r => new GlobalTraceFlagInfo(
                SqlRead.Int(r, "TraceFlag"),
                SqlRead.Bool(r, "Global")),
            cancellationToken).ConfigureAwait(false);
    }
}

internal static class SqlRead
{
    public static string String(SqlDataReader reader, string column)
    {
        var value = reader[column];
        return value is DBNull ? string.Empty : Convert.ToString(value) ?? string.Empty;
    }

    public static string? NullableString(SqlDataReader reader, string column)
    {
        var value = reader[column];
        return value is DBNull ? null : Convert.ToString(value);
    }

    public static bool Bool(SqlDataReader reader, string column)
    {
        var value = reader[column];
        if (value is DBNull)
        {
            return false;
        }

        return value switch
        {
            bool b => b,
            byte bt => bt == 1,
            short s => s == 1,
            int i => i == 1,
            long l => l == 1,
            _ => Convert.ToBoolean(value),
        };
    }

    public static int Int(SqlDataReader reader, string column)
    {
        var value = reader[column];
        return value is DBNull ? 0 : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static long Long(SqlDataReader reader, string column)
    {
        var value = reader[column];
        return value is DBNull ? 0L : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static decimal Decimal(SqlDataReader reader, string column)
    {
        var value = reader[column];
        return value is DBNull ? 0m : Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static decimal? NullableDecimal(SqlDataReader reader, string column)
    {
        var value = reader[column];
        return value is DBNull ? null : Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static double Double(SqlDataReader reader, string column)
    {
        var value = reader[column];
        return value is DBNull ? 0 : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static DateTimeOffset? NullableDateTimeOffset(SqlDataReader reader, string column)
    {
        var value = reader[column];
        if (value is DBNull)
        {
            return null;
        }

        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => DateTimeOffset.TryParse(
                Convert.ToString(value),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed)
                ? parsed
                : null,
        };
    }
}
