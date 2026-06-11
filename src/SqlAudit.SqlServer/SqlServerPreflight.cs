using Microsoft.Data.SqlClient;

namespace SqlAudit.SqlServer;

/// <summary>
/// Handles the initial connectivity and permissions verification before a full audit scan.
/// </summary>
public static class SqlServerPreflight
{
    /// <summary>
    /// Executes a preflight query to verify connectivity, SQL Server version, and `VIEW SERVER STATE` permissions.
    /// </summary>
    /// <param name="connectionString">The connection string to test.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="commandTimeoutSeconds">The command timeout in seconds for the preflight query.</param>
    /// <returns>A preflight result indicating success or detailing permission issues.</returns>
    public static async Task<SqlServerPreflightResult> RunAsync(string connectionString, CancellationToken cancellationToken, int commandTimeoutSeconds = 30)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                CONVERT(sysname, @@SERVERNAME) AS server_name,
                DB_NAME() AS database_name,
                CONVERT(nvarchar(256), SERVERPROPERTY('Edition')) AS edition,
                CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS product_version
        """;

        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = commandTimeoutSeconds,
        };
        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
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
