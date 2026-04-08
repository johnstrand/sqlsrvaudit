using SqlAudit.Core.Models;
using System.Text;

namespace SqlAudit.Reporting;

public static class SqlFixScriptRenderer
{
    public static RenderedFixScripts Render(AuditReport report)
    {
        var noWindow = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var requiresWindow = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var combined = new StringBuilder();

        combined.AppendLine("-- SQL Audit remediation script bundle");
        combined.AppendLine($"-- Database: {report.DatabaseName}");
        combined.AppendLine($"-- Generated (UTC): {report.CapturedAtUtc:u}");
        combined.AppendLine();

        foreach (var finding in report.Findings.Where(f => !string.IsNullOrWhiteSpace(f.FixScript)))
        {
            var slug = BuildSlug(finding.Id, finding.Title);
            var fileName = $"{slug}.sql";
            var script = finding.FixScript!.Trim();

            if (finding.ServiceWindow.RequiresServiceWindow)
                requiresWindow[fileName] = script + Environment.NewLine;
            else
                noWindow[fileName] = script + Environment.NewLine;

            combined.AppendLine($"-- Finding: {finding.Id} - {finding.Title}");
            combined.AppendLine($"-- RequiresServiceWindow: {finding.ServiceWindow.RequiresServiceWindow}");
            combined.AppendLine($"-- Reason: {finding.ServiceWindow.Reason}");
            combined.AppendLine(script);
            combined.AppendLine("GO");
            combined.AppendLine();
        }

        return new RenderedFixScripts(combined.ToString(), noWindow, requiresWindow);
    }

    private static string BuildSlug(string id, string title)
    {
        var raw = $"{id}-{title}".ToLowerInvariant();
        var chars = raw
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        var collapsed = new string(chars)
            .Replace("--", "-", StringComparison.Ordinal)
            .Trim('-');

        return collapsed.Length <= 100 ? collapsed : collapsed[..100];
    }
}

public sealed record RenderedFixScripts(
    string CombinedScript,
    IReadOnlyDictionary<string, string> NoWindowScripts,
    IReadOnlyDictionary<string, string> RequiresWindowScripts);
