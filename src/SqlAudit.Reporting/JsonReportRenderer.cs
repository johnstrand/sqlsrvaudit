using SqlAudit.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlAudit.Reporting;

/// <summary>
/// Serializes the audit report to a standardized JSON format for programmatic consumption.
/// </summary>
public static class JsonReportRenderer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Render(AuditReport report) => JsonSerializer.Serialize(report, SerializerOptions);
}
