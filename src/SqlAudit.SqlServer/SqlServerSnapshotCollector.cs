using Microsoft.Data.SqlClient;
using SqlAudit.Core.Models;
using System.Globalization;

namespace SqlAudit.SqlServer;

public sealed class SqlServerSnapshotCollector
{
    public static async Task<DatabaseSnapshot> CollectAsync(string connectionString, AuditProfile profile, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var serverInfo = await ReadServerInfoAsync(connection, cancellationToken).ConfigureAwait(false);
        var includePhysical = profile == AuditProfile.Deep;
        var includeStatistics = profile == AuditProfile.Deep;

        return new DatabaseSnapshot
        {
            ServerName = serverInfo.ServerName,
            DatabaseName = serverInfo.DatabaseName,
            Edition = serverInfo.Edition,
            ProductVersion = serverInfo.ProductVersion,
            IsAzureSql = serverInfo.IsAzureSql,
            AutoCreateStatisticsOn = serverInfo.AutoCreateStatisticsOn,
            AutoUpdateStatisticsOn = serverInfo.AutoUpdateStatisticsOn,
            Tables = await ReadTablesAsync(connection, cancellationToken).ConfigureAwait(false),
            Indexes = await ReadIndexesAsync(connection, cancellationToken).ConfigureAwait(false),
            IndexUsage = await ReadIndexUsageAsync(connection, cancellationToken).ConfigureAwait(false),
            IndexPhysicalStats = includePhysical
                ? await ReadIndexPhysicalStatsAsync(connection, cancellationToken).ConfigureAwait(false)
                : [],
            ForeignKeys = await ReadForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false),
            Statistics = includeStatistics
                ? await ReadStatisticsAsync(connection, cancellationToken).ConfigureAwait(false)
                : [],
            IdentityColumns = await ReadIdentityColumnsAsync(connection, cancellationToken).ConfigureAwait(false)
        };
    }

    private static async Task<ServerInfo> ReadServerInfoAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                CONVERT(sysname, @@SERVERNAME) AS server_name,
                DB_NAME() AS database_name,
                CONVERT(nvarchar(256), SERVERPROPERTY('Edition')) AS edition,
                CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS product_version,
                CASE WHEN CONVERT(int, DATABASEPROPERTYEX(DB_NAME(), 'IsAutoCreateStatistics')) = 1 THEN 1 ELSE 0 END AS auto_create_stats,
                CASE WHEN CONVERT(int, DATABASEPROPERTYEX(DB_NAME(), 'IsAutoUpdateStatistics')) = 1 THEN 1 ELSE 0 END AS auto_update_stats,
                CASE WHEN CONVERT(int, SERVERPROPERTY('EngineEdition')) IN (5, 8) THEN 1 ELSE 0 END AS is_azure
        """;

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Unable to read SQL Server metadata.");
        }

        return new ServerInfo(
            SqlRead.String(reader, "server_name"),
            SqlRead.String(reader, "database_name"),
            SqlRead.String(reader, "edition"),
            SqlRead.String(reader, "product_version"),
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

    private static async Task<IReadOnlyList<T>> ReadListAsync<T>(
        SqlConnection connection,
        string sql,
        Func<SqlDataReader, T> map,
        CancellationToken cancellationToken)
    {
        var rows = new List<T>();

        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 120
        };

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
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
        bool AutoCreateStatisticsOn,
        bool AutoUpdateStatisticsOn,
        bool IsAzureSql);
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
            _ => Convert.ToBoolean(value)
        };
    }

    public static int Int(SqlDataReader reader, string column)
    {
        var value = reader[column];
        return value is DBNull ? 0 : Convert.ToInt32(value);
    }

    public static long Long(SqlDataReader reader, string column)
    {
        var value = reader[column];
        return value is DBNull ? 0L : Convert.ToInt64(value);
    }

    public static decimal Decimal(SqlDataReader reader, string column)
    {
        var value = reader[column];
        return value is DBNull ? 0m : Convert.ToDecimal(value);
    }

    public static decimal? NullableDecimal(SqlDataReader reader, string column)
    {
        var value = reader[column];
        return value is DBNull ? null : Convert.ToDecimal(value);
    }

    public static double Double(SqlDataReader reader, string column)
    {
        var value = reader[column];
        return value is DBNull ? 0 : Convert.ToDouble(value);
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
                : null
        };
    }
}
