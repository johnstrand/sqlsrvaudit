using SqlAudit.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlAudit.Reporting;

public static class JsonReportRenderer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Render(AuditReport report) => JsonSerializer.Serialize(report, SerializerOptions);
}
