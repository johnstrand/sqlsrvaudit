using SqlAudit.Core.Models;
using System.Text.Json;

namespace SqlAudit.Reporting;

public static class DataModelJsonRenderer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public static string Render(DatabaseSnapshot snapshot) => JsonSerializer.Serialize(snapshot, SerializerOptions);
}
