using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SqlAudit.SqlServer;

public sealed class SqlServerPreflight
{
    public async Task<SqlServerPreflightResult> RunAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string sql = """
            SELECT
                CONVERT(sysname, @@SERVERNAME) AS server_name,
                DB_NAME() AS database_name,
                CONVERT(nvarchar(256), SERVERPROPERTY('Edition')) AS edition,
                CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS product_version
        """;

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Preflight query returned no rows.");
        }

        return new SqlServerPreflightResult(
            Convert.ToString(reader["server_name"]) ?? string.Empty,
            Convert.ToString(reader["database_name"]) ?? string.Empty,
            Convert.ToString(reader["edition"]) ?? string.Empty,
            Convert.ToString(reader["product_version"]) ?? string.Empty);
    }
}

public sealed record SqlServerPreflightResult(
    string ServerName,
    string DatabaseName,
    string Edition,
    string ProductVersion);
