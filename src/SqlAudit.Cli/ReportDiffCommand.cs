using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SqlAudit.Core.Models;

namespace SqlAudit.Cli;

internal static class ReportDiffCommand
{
    public static int Run(CliOptions options)
    {
        if (!string.Equals(options.Subcommand, "diff", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Unknown report subcommand: {options.Subcommand}");
            Console.Error.WriteLine("Use 'report diff'.");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(options.PreviousReportPath) || string.IsNullOrWhiteSpace(options.CurrentReportPath))
        {
            Console.Error.WriteLine("Missing required options for report diff.");
            Console.Error.WriteLine("Usage: report diff --previous <path> --current <path>");
            return 2;
        }

        var previousPath = Path.GetFullPath(options.PreviousReportPath);
        var currentPath = Path.GetFullPath(options.CurrentReportPath);

        var previous = ReadReport(previousPath);
        var current = ReadReport(currentPath);

        var diff = Analyze(previous, current);
        PrintDiff(previousPath, currentPath, diff);
        return 0;
    }

    private static AuditReport ReadReport(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Report file not found: {path}");
        }

        var json = File.ReadAllText(path);
        var report = JsonSerializer.Deserialize<AuditReport>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        });

        if (report is null)
        {
            throw new InvalidOperationException($"Could not parse report file: {path}");
        }

        return report;
    }

    internal static ReportDiffResult Analyze(AuditReport previous, AuditReport current)
    {
        var previousMap = previous.Findings
            .GroupBy(BuildKey)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var currentMap = current.Findings
            .GroupBy(BuildKey)
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

    private static void PrintDiff(string previousPath, string currentPath, ReportDiffResult diff)
    {
        Console.WriteLine("Report diff");
        Console.WriteLine($"  Previous : {previousPath}");
        Console.WriteLine($"  Current  : {currentPath}");
        Console.WriteLine();
        Console.WriteLine($"  New findings      : {diff.NewFindings.Count}");
        Console.WriteLine($"  Fixed findings    : {diff.FixedFindings.Count}");
        Console.WriteLine($"  Regressed severity: {diff.Regressed.Count}");
        Console.WriteLine($"  Improved severity : {diff.Improved.Count}");

        PrintFindingList("Top new findings", diff.NewFindings.OrderBy(f => f.Severity).Take(10).ToArray());
        PrintFindingList("Top fixed findings", diff.FixedFindings.OrderBy(f => f.Severity).Take(10).ToArray());

        if (diff.Regressed.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Regressed severity:");
            foreach (var item in diff.Regressed.Take(10))
            {
                Console.WriteLine($"  - {item.Current.Id} {item.Current.DatabaseObject} ({item.Previous.Severity} -> {item.Current.Severity})");
            }
        }
    }

    private static void PrintFindingList(string title, IReadOnlyList<AuditFinding> findings)
    {
        Console.WriteLine();
        Console.WriteLine($"{title}:");
        if (findings.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }

        foreach (var finding in findings)
        {
            Console.WriteLine($"  - {finding.Id} {finding.DatabaseObject} [{finding.Severity}]");
        }
    }

    private static string BuildKey(AuditFinding finding) => $"{finding.Id}::{finding.DatabaseObject}";
}

internal sealed record ReportDiffResult(
    IReadOnlyList<AuditFinding> NewFindings,
    IReadOnlyList<AuditFinding> FixedFindings,
    IReadOnlyList<(AuditFinding Previous, AuditFinding Current)> Regressed,
    IReadOnlyList<(AuditFinding Previous, AuditFinding Current)> Improved);
