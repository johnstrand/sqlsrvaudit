using SqlAudit.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlAudit.Cli;

internal static class ReportDiffCommand
{
    public static int Run(CliOptions options) => Run(options, options.Verbosity);

    public static int Run(CliOptions options, LogVerbosity verbosity)
    {
        if (!string.Equals(options.Subcommand, "diff", StringComparison.OrdinalIgnoreCase))
        {
            ScanOutput.ShowError($"Unknown report subcommand: {options.Subcommand}", hint: "Use 'report diff'.");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(options.PreviousReportPath) || string.IsNullOrWhiteSpace(options.CurrentReportPath))
        {
            ScanOutput.ShowError("Missing required options for report diff.", hint: "Usage: report diff --previous <path> --current <path>");
            return 2;
        }

        var previousPath = Path.GetFullPath(options.PreviousReportPath);
        var currentPath = Path.GetFullPath(options.CurrentReportPath);

        var previous = ReadReport(previousPath);
        var current = ReadReport(currentPath);

        var diff = Analyze(previous, current);
        switch (options.DiffFormat)
        {
            case ReportDiffFormat.Json:
                ScanOutput.PrintReportDiffJson(diff, previousPath, currentPath, verbosity);
                break;
            case ReportDiffFormat.Markdown:
                ScanOutput.PrintReportDiffMarkdown(diff, previousPath, currentPath, verbosity);
                break;
            default:
                ScanOutput.PrintReportDiff(diff, previousPath, currentPath, verbosity);
                break;
        }

        if (options.FailOnRegression && (diff.NewFindings.Count > 0 || diff.Regressed.Count > 0))
        {
            ScanOutput.ShowError(
                $"Regression detected: {diff.NewFindings.Count} new finding(s) and {diff.Regressed.Count} regressed finding(s).");
            return 3;
        }

        return 0;
    }

    public static int Execute(CliOptions options, bool? verbose = null, bool? quiet = null)
    {
        var verbosity = options.Verbosity;
        if (verbose == true)
        {
            verbosity = LogVerbosity.Verbose;
        }
        else if (quiet == true)
        {
            verbosity = LogVerbosity.Quiet;
        }

        return Run(options, verbosity);
    }

    private static AuditReport ReadReport(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Report file not found: {path}");
        }

        var json = File.ReadAllText(path);
#pragma warning disable CA1869 // Cache and reuse 'JsonSerializerOptions' instances
        var report = JsonSerializer.Deserialize<AuditReport>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        }) ?? throw new InvalidOperationException($"Could not parse report file: {path}");
#pragma warning restore CA1869 // Cache and reuse 'JsonSerializerOptions' instances
        return report;
    }

    internal static ReportDiffResult Analyze(AuditReport previous, AuditReport current)
    {
        var previousMap = previous.Findings
            .GroupBy(BuildKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var currentMap = current.Findings
            .GroupBy(BuildKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var added = new List<AuditFinding>();
        var removed = new List<AuditFinding>();
        var regressed = new List<(AuditFinding Previous, AuditFinding Current)>();
        var improved = new List<(AuditFinding Previous, AuditFinding Current)>();

        foreach (var currentPair in currentMap)
        {
            if (!previousMap.TryGetValue(currentPair.Key, out var oldFinding))
            {
                added.Add(currentPair.Value);
                continue;
            }

            if (currentPair.Value.Severity < oldFinding.Severity)
            {
                regressed.Add((oldFinding, currentPair.Value));
            }
            else if (currentPair.Value.Severity > oldFinding.Severity)
            {
                improved.Add((oldFinding, currentPair.Value));
            }
        }

        removed.AddRange(previousMap
            .Where(previousPair => !currentMap.ContainsKey(previousPair.Key))
            .Select(previousPair => previousPair.Value));

        return new ReportDiffResult(added, removed, regressed, improved);
    }

    private static string BuildKey(AuditFinding finding) => $"{finding.Id}::{finding.DatabaseObject}";
}

internal sealed record ReportDiffResult(
    IReadOnlyList<AuditFinding> NewFindings,
    IReadOnlyList<AuditFinding> FixedFindings,
    IReadOnlyList<(AuditFinding Previous, AuditFinding Current)> Regressed,
    IReadOnlyList<(AuditFinding Previous, AuditFinding Current)> Improved);
