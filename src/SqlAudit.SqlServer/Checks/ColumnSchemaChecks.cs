using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;
using System.Globalization;

namespace SqlAudit.SqlServer.Checks;

internal sealed class NullableColumnWithNoNullsCheck : IHealthCheck
{
    public string Id => "COL-001";

    public string Title => "Nullable columns containing no NULL values";

    public string Category => "Schema";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        if (context.Snapshot.ColumnNullStats.Count == 0)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var tableRowCounts = context.Snapshot.Tables
            .ToDictionary(t => t.ObjectId, t => t.RowCount);

        var findings = context.Snapshot.ColumnNullStats
            .Select(col =>
            {
                var tableName = SqlName.Table(col.SchemaName, col.TableName);
                var quotedColumn = $"[{col.ColumnName}]";
                tableRowCounts.TryGetValue(col.ObjectId, out var rowCount);

                return new AuditFinding
                {
                    Id = $"COL-001-{col.ObjectId}-{col.ColumnName.GetHashCode(StringComparison.OrdinalIgnoreCase):X}",
                    Title = "Nullable column contains no NULL values",
                    Category = Category,
                    Severity = AuditSeverity.Info,
                    DatabaseObject = $"{tableName}.{quotedColumn}",
                    Description = $"Column {quotedColumn} on {tableName} is declared NULL but no NULL values were found across {rowCount.ToString("N0", CultureInfo.InvariantCulture)} sampled rows.",
                    Impact = "Nullable columns carry a small per-row storage overhead and weaken NOT NULL guarantees. If nullability is unintentional it may also indicate a schema design issue.",
                    Recommendation = "If the column is never intended to be null, alter it to NOT NULL. Verify application code does not rely on implicit NULLs before making the change.",
                    ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                        AuditOperationRisk.ConstraintValidation,
                        "Adding NOT NULL validates all existing rows and may lock the table briefly."),
                    FixScript = $"""
                        -- RequiresServiceWindow: true
                        -- Reason: Altering column nullability validates all rows.
                        -- Verify no application path inserts NULL before running.
                        ALTER TABLE {tableName}
                        ALTER COLUMN {quotedColumn} <data_type> NOT NULL;
                        """,
                    Evidence =
                    [
                        new FindingEvidence("SampledRows", rowCount.ToString("N0", CultureInfo.InvariantCulture)),
                        new FindingEvidence("NullsFound", "0"),
                    ],
                };
            })
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class OversizedStringColumnCheck : IHealthCheck
{
    // nvarchar stores 2 bytes per char; max_length -1 = MAX
    private const int MaxNvarcharBytes = 2000;  // > nvarchar(1000)
    private const int MaxVarcharBytes = 2000;   // > varchar(2000)

    public string Id => "COL-002";

    public string Title => "Oversized string column declarations";

    public string Category => "Schema";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();

        foreach (var col in context.Snapshot.Columns)
        {
            if (!IsOversized(col, out var declaredDescription, out var severity))
            {
                continue;
            }

            var tableName = SqlName.Table(col.SchemaName, col.TableName);
            var quotedColumn = $"[{col.ColumnName}]";

            findings.Add(new AuditFinding
            {
                Id = $"COL-002-{col.ObjectId}-{col.ColumnName.GetHashCode(StringComparison.OrdinalIgnoreCase):X}",
                Title = "String column declared wider than typically necessary",
                Category = Category,
                Severity = severity,
                DatabaseObject = $"{tableName}.{quotedColumn}",
                Description = $"Column {quotedColumn} on {tableName} is declared as {col.DataType}({declaredDescription}). Very wide or unbounded string columns consume unnecessary storage per row and may prevent row-level compression and covering index inclusion.",
                Impact = "Oversized string columns inflate row size, reduce rows-per-page, and can prevent index seeks on covering indexes. MAX columns cannot be included in index keys.",
                Recommendation = "Determine the true maximum length required for this column and constrain it accordingly (e.g., nvarchar(200) instead of nvarchar(max)).",
                ServiceWindow = ServiceWindowAdvisor.ForConservativePolicy(
                    AuditOperationRisk.ConstraintValidation,
                    "Reducing column width validates existing data lengths and locks the table briefly."),
                FixScript = $"""
                    -- RequiresServiceWindow: true
                    -- Reason: Shrinking column width validates all existing values.
                    -- TODO: Replace <new_length> with the appropriate maximum length.
                    ALTER TABLE {tableName}
                    ALTER COLUMN {quotedColumn} {col.DataType}(<new_length>){(col.IsNullable ? " NULL" : " NOT NULL")};
                    """,
                Evidence =
                [
                    new FindingEvidence("DataType", col.DataType),
                    new FindingEvidence("DeclaredLength", declaredDescription),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }

    private static bool IsOversized(ColumnInfo col, out string declaredDescription, out AuditSeverity severity)
    {
        declaredDescription = string.Empty;
        severity = AuditSeverity.Info;

        var type = col.DataType.ToLowerInvariant();

        if (type is "nvarchar" or "nchar")
        {
            if (col.MaxLength == -1)
            {
                declaredDescription = "max";
                severity = AuditSeverity.Medium;
                return true;
            }

            if (col.MaxLength > MaxNvarcharBytes)
            {
                var charLen = col.MaxLength / 2;
                declaredDescription = charLen.ToString(CultureInfo.InvariantCulture);
                severity = AuditSeverity.Info;
                return true;
            }

            return false;
        }

        if (type is "varchar" or "char")
        {
            if (col.MaxLength == -1)
            {
                declaredDescription = "max";
                severity = AuditSeverity.Medium;
                return true;
            }

            if (col.MaxLength > MaxVarcharBytes)
            {
                declaredDescription = col.MaxLength.ToString(CultureInfo.InvariantCulture);
                severity = AuditSeverity.Info;
                return true;
            }

            return false;
        }

        return false;
    }
}
