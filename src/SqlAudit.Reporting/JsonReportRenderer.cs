using System.Text.Json;
using System.Text.Json.Serialization;
using SqlAudit.Core.Models;

namespace SqlAudit.Reporting;

public sealed class JsonReportRenderer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Render(AuditReport report) => JsonSerializer.Serialize(report, SerializerOptions);
}
