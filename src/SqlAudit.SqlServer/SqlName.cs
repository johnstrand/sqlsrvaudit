namespace SqlAudit.SqlServer;

internal static class SqlName
{
    public static string Database(string databaseName) => $"[{Escape(databaseName)}]";

    public static string Table(string schemaName, string tableName) =>
        $"[{Escape(schemaName)}].[{Escape(tableName)}]";

    public static string Index(string indexName) => $"[{Escape(indexName)}]";

    public static string Constraint(string constraintName) => $"[{Escape(constraintName)}]";

    public static string Column(string columnName) => $"[{Escape(columnName)}]";

    public static string Statistic(string statName) => $"[{Escape(statName)}]";

    public static string StringLiteral(string value) => $"N'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    public static string ObjectNameSuffix(string name)
    {
        var cleaned = name.Replace(' ', '_')
            .Replace('-', '_')
            .Replace("]", string.Empty, StringComparison.Ordinal)
            .Replace("[", string.Empty, StringComparison.Ordinal);

        return cleaned.Length <= 110 ? cleaned : cleaned[..110];
    }

    private static string Escape(string value) => value.Replace("]", "]]", StringComparison.Ordinal);
}
