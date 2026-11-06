using Spectre.Console;
using SqlAudit.Core.Models;
using SqlAudit.SqlServer;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlAudit.Cli;

internal static class ScanOutput
{
    private static TextWriter? _lastErrorWriter;
    private static IAnsiConsole? _errorConsole;

    internal static IAnsiConsole ErrorConsole
    {
        get
        {
            var current = Console.Error;
            if (_errorConsole is null || !ReferenceEquals(current, _lastErrorWriter))
            {
                _lastErrorWriter = current;
                _errorConsole = AnsiConsole.Create(new AnsiConsoleSettings
                {
                    Out = new AnsiConsoleOutput(current),
                });
            }

            return _errorConsole;
        }
        set
        {
            _errorConsole = value;
            _lastErrorWriter = Console.Error;
        }
    }

    internal static void ResetErrorConsole()
    {
        _errorConsole = null;
        _lastErrorWriter = null;
    }

    public static void ShowError(string message, string? details = null, string? hint = null)
    {
        ErrorConsole.MarkupLine($"[red bold]Error:[/] {Markup.Escape(message)}");
        if (!string.IsNullOrWhiteSpace(hint))
        {
            ErrorConsole.MarkupLine($"[grey]{Markup.Escape(hint)}[/]");
        }

        if (!string.IsNullOrWhiteSpace(details))
        {
            var formattedDetails = details.StartsWith("Details:", StringComparison.OrdinalIgnoreCase)
                ? details
                : $"Details: {details}";
            ErrorConsole.MarkupLine($"[grey]{Markup.Escape(formattedDetails)}[/]");
        }
    }

    public static void ShowWarning(string message, string? details = null)
    {
        var formatted = message.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase)
            ? $"[yellow]{Markup.Escape(message)}[/]"
            : $"[yellow]Warning:[/] {Markup.Escape(message)}";

        ErrorConsole.MarkupLine(formatted);

        if (!string.IsNullOrWhiteSpace(details))
        {
            var formattedDetails = details.StartsWith("Details:", StringComparison.OrdinalIgnoreCase)
                ? details
                : $"Details: {details}";
            ErrorConsole.MarkupLine($"[grey]{Markup.Escape(formattedDetails)}[/]");
        }
    }

    public static void ShowInfo(string message)
    {
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(message)}[/]");
    }

    public static void ShowSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(message)}");
    }

    public static void PrintDeletingFixScript(LogVerbosity verbosity, string file)
    {
        if (verbosity >= LogVerbosity.Normal)
        {
            AnsiConsole.MarkupLine($"[grey]Deleting existing fix script:[/] [green]{Markup.Escape(file)}[/]");
        }
    }

    public static void PrintSuppressionsValidationResult(string fullPath, SuppressionValidationResult result)
    {
        AnsiConsole.MarkupLine($"[grey]Suppressions file:[/] [cyan]{Markup.Escape(fullPath)}[/]");
        AnsiConsole.MarkupLine($"[grey]Rules parsed     :[/] [white]{result.RuleCount}[/]");

        foreach (var warning in result.Warnings)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");
        }

        if (result.IsValid)
        {
            AnsiConsole.MarkupLine("[green]Validation passed.[/]");
            return;
        }

        ErrorConsole.MarkupLine("[red]Validation failed.[/]");
        foreach (var error in result.Errors)
        {
            ErrorConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(error)}");
        }
    }

    public static void PrintReportDiff(
        ReportDiffResult diff,
        string previousPath,
        string currentPath,
        LogVerbosity verbosity)
    {
        if (verbosity == LogVerbosity.Quiet)
        {
            return;
        }

        AnsiConsole.Write(new Rule("[cyan bold]Report diff[/]").LeftJustified());
        AnsiConsole.MarkupLine($"  [grey]Previous :[/] [cyan]{Markup.Escape(previousPath)}[/]");
        AnsiConsole.MarkupLine($"  [grey]Current  :[/] [cyan]{Markup.Escape(currentPath)}[/]");
        AnsiConsole.WriteLine();

        var summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[grey]Metric[/]"))
            .AddColumn(new TableColumn("[grey]Count[/]").RightAligned());

        summaryTable.AddRow("[grey]New findings[/]", diff.NewFindings.Count > 0 ? $"[yellow]{diff.NewFindings.Count}[/]" : "[grey]0[/]");
        summaryTable.AddRow("[grey]Fixed findings[/]", diff.FixedFindings.Count > 0 ? $"[green]{diff.FixedFindings.Count}[/]" : "[grey]0[/]");
        summaryTable.AddRow("[grey]Regressed severity[/]", diff.Regressed.Count > 0 ? $"[red]{diff.Regressed.Count}[/]" : "[grey]0[/]");
        summaryTable.AddRow("[grey]Improved severity[/]", diff.Improved.Count > 0 ? $"[green]{diff.Improved.Count}[/]" : "[grey]0[/]");

        AnsiConsole.Write(summaryTable);

        var isVerbose = verbosity == LogVerbosity.Verbose;
        var limit = isVerbose ? int.MaxValue : 10;

        PrintDiffFindingList(
            isVerbose ? "New findings" : "Top new findings",
            [.. diff.NewFindings.OrderBy(f => f.Severity).Take(limit)]);

        PrintDiffFindingList(
            isVerbose ? "Fixed findings" : "Top fixed findings",
            [.. diff.FixedFindings.OrderBy(f => f.Severity).Take(limit)]);

        if (diff.Regressed.Count > 0)
        {
            var regressedList = diff.Regressed.Take(limit).ToList();
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red bold]{(isVerbose ? "Regressed severity:" : "Top regressed severity:")}[/]");
            foreach (var (prev, curr) in regressedList)
            {
                var prevColor = SeverityMarkup.GetValueOrDefault(prev.Severity, "grey");
                var currColor = SeverityMarkup.GetValueOrDefault(curr.Severity, "grey");
                AnsiConsole.MarkupLine($"  [grey]-[/] [cyan]{Markup.Escape(curr.Id)}[/] [white]{Markup.Escape(curr.DatabaseObject)}[/] ([{prevColor}]{prev.Severity}[/] -> [{currColor}]{curr.Severity}[/])");
            }
        }

        if (diff.Improved.Count > 0)
        {
            var improvedList = diff.Improved.Take(limit).ToList();
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green bold]{(isVerbose ? "Improved severity:" : "Top improved severity:")}[/]");
            foreach (var (prev, curr) in improvedList)
            {
                var prevColor = SeverityMarkup.GetValueOrDefault(prev.Severity, "grey");
                var currColor = SeverityMarkup.GetValueOrDefault(curr.Severity, "grey");
                AnsiConsole.MarkupLine($"  [grey]-[/] [cyan]{Markup.Escape(curr.Id)}[/] [white]{Markup.Escape(curr.DatabaseObject)}[/] ([{prevColor}]{prev.Severity}[/] -> [{currColor}]{curr.Severity}[/])");
            }
        }

        AnsiConsole.Write(new Rule().RuleStyle("grey"));
    }

    public static void PrintReportDiffJson(
        ReportDiffResult diff,
        string previousPath,
        string currentPath,
        LogVerbosity verbosity)
    {
        if (verbosity == LogVerbosity.Quiet)
        {
            return;
        }

        var payload = new
        {
            PreviousPath = previousPath,
            CurrentPath = currentPath,
            Summary = new
            {
                NewCount = diff.NewFindings.Count,
                FixedCount = diff.FixedFindings.Count,
                RegressedCount = diff.Regressed.Count,
                ImprovedCount = diff.Improved.Count,
            },
            NewFindings = diff.NewFindings,
            FixedFindings = diff.FixedFindings,
            Regressed = diff.Regressed.Select(r => new
            {
                r.Current.Id,
                r.Current.DatabaseObject,
                PreviousSeverity = r.Previous.Severity,
                CurrentSeverity = r.Current.Severity,
                Finding = r.Current,
            }).ToArray(),
            Improved = diff.Improved.Select(i => new
            {
                i.Current.Id,
                i.Current.DatabaseObject,
                PreviousSeverity = i.Previous.Severity,
                CurrentSeverity = i.Current.Severity,
                Finding = i.Current,
            }).ToArray(),
        };

#pragma warning disable CA1869 // Cache and reuse 'JsonSerializerOptions' instances
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        });
#pragma warning restore CA1869 // Cache and reuse 'JsonSerializerOptions' instances

        AnsiConsole.Profile.Out.Writer.WriteLine(json);
    }

    public static void PrintReportDiffMarkdown(
        ReportDiffResult diff,
        string previousPath,
        string currentPath,
        LogVerbosity verbosity)
    {
        if (verbosity == LogVerbosity.Quiet)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Report Diff");
        sb.AppendLine();
        sb.AppendLine($"- **Previous**: `{previousPath}`");
        sb.AppendLine($"- **Current**: `{currentPath}`");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Count |");
        sb.AppendLine("| --- | ---: |");
        sb.AppendLine($"| New findings | {diff.NewFindings.Count} |");
        sb.AppendLine($"| Fixed findings | {diff.FixedFindings.Count} |");
        sb.AppendLine($"| Regressed severity | {diff.Regressed.Count} |");
        sb.AppendLine($"| Improved severity | {diff.Improved.Count} |");

        AppendMarkdownFindingSection(sb, "New Findings", diff.NewFindings);
        AppendMarkdownFindingSection(sb, "Fixed Findings", diff.FixedFindings);
        AppendMarkdownSeverityTransitionSection(sb, "Regressed Severity", diff.Regressed);
        AppendMarkdownSeverityTransitionSection(sb, "Improved Severity", diff.Improved);

        AnsiConsole.Profile.Out.Writer.Write(sb.ToString());
    }

    private static void AppendMarkdownFindingSection(StringBuilder sb, string title, IReadOnlyList<AuditFinding> findings)
    {
        sb.AppendLine();
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        if (findings.Count == 0)
        {
            sb.AppendLine("_(none)_");
            return;
        }

        foreach (var finding in findings.OrderBy(f => f.Severity))
        {
            sb.AppendLine($"- `{finding.Id}` **{finding.DatabaseObject}** [{finding.Severity}] — {finding.Title}");
        }
    }

    private static void AppendMarkdownSeverityTransitionSection(
        StringBuilder sb,
        string title,
        IReadOnlyList<(AuditFinding Previous, AuditFinding Current)> transitions)
    {
        if (transitions.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        foreach (var (prev, curr) in transitions)
        {
            sb.AppendLine($"- `{curr.Id}` **{curr.DatabaseObject}** ({prev.Severity} -> {curr.Severity})");
        }
    }

    private static void PrintDiffFindingList(string title, IReadOnlyList<AuditFinding> findings)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[cyan bold]{Markup.Escape(title)}:[/]");
        if (findings.Count == 0)
        {
            AnsiConsole.MarkupLine("  [grey](none)[/]");
            return;
        }

        foreach (var finding in findings)
        {
            var color = SeverityMarkup.GetValueOrDefault(finding.Severity, "white");
            AnsiConsole.MarkupLine($"  [grey]-[/] [cyan]{Markup.Escape(finding.Id)}[/] [white]{Markup.Escape(finding.DatabaseObject)}[/] [{color}][[{finding.Severity}]][/]");
        }
    }
    private static readonly Dictionary<AuditSeverity, string> SeverityMarkup = new()
    {
        [AuditSeverity.Critical] = "red",
        [AuditSeverity.High] = "darkorange3",
        [AuditSeverity.Medium] = "yellow",
        [AuditSeverity.Low] = "deepskyblue1",
        [AuditSeverity.Info] = "grey",
    };

    private static bool IsInteractive(LogVerbosity verbosity) =>
        verbosity != LogVerbosity.Quiet && AnsiConsole.Profile.Capabilities.Interactive;

    public static void PrintBanner(LogVerbosity verbosity)
    {
        if (verbosity == LogVerbosity.Quiet)
        {
            return;
        }

        AnsiConsole.Write(new Rule("[cyan bold]SqlAudit[/]  [grey]SQL Server Health Audit[/]").LeftJustified());
    }

    public static void PrintRunConfiguration(LogVerbosity verbosity, EffectiveRunOptions resolved, int activeChecks)
    {
        if (verbosity == LogVerbosity.Quiet)
        {
            return;
        }

        AnsiConsole.MarkupLine($"  [grey]Profile      :[/] [cyan]{resolved.Profile.ToString().ToLowerInvariant()}[/]");
        AnsiConsole.MarkupLine($"  [grey]Output format:[/] [cyan]{resolved.Format.ToString().ToLowerInvariant()}[/]");
        AnsiConsole.MarkupLine($"  [grey]Data model   :[/] [cyan]{(resolved.OutputDataModel ? "enabled" : "disabled")}[/]");
        AnsiConsole.MarkupLine($"  [grey]Active checks:[/] [cyan]{activeChecks}[/]");
        AnsiConsole.MarkupLine($"  [grey]Output dir   :[/] [cyan]{Markup.Escape(resolved.OutputDirectory)}[/]");
        AnsiConsole.MarkupLine($"  [grey]Suppressions :[/] [cyan]{Markup.Escape(resolved.SuppressionsPath ?? "(none)")}[/]");
        AnsiConsole.MarkupLine(
            $"  [grey]Excl. schemas:[/] [cyan]{Markup.Escape(resolved.ExcludeSchemas is null ? "(none)" : string.Join(", ", resolved.ExcludeSchemas))}[/]");
        AnsiConsole.MarkupLine(
            $"  [grey]Excl. tables :[/] [cyan]{Markup.Escape(resolved.ExcludeTables is null ? "(none)" : string.Join(", ", resolved.ExcludeTables))}[/]");
    }

    public static Stopwatch StartStep(LogVerbosity verbosity, int current, int total, string title)
    {
        if (verbosity != LogVerbosity.Quiet)
        {
            AnsiConsole.MarkupLine($"[cyan][[{current}/{total}]][/] {Markup.Escape(title)}");
        }

        return Stopwatch.StartNew();
    }

    public static void EndStep(LogVerbosity verbosity, Stopwatch sw, string message)
    {
        sw.Stop();
        if (verbosity != LogVerbosity.Quiet)
        {
            AnsiConsole.MarkupLine(
                $"  [green]✓[/]  {Markup.Escape(message)}  [grey]({sw.Elapsed:hh\\:mm\\:ss})[/]");
        }
    }

    public static async Task<SqlServerAuditRunResult> RunAuditWithProgressAsync(
        LogVerbosity verbosity,
        SqlServerAuditor auditor,
        EffectiveRunOptions resolved,
        int totalChecks,
        CancellationToken cancellationToken)
    {
        if (!IsInteractive(verbosity))
        {
            return await RunAuditPlainAsync(verbosity, auditor, resolved, cancellationToken)
                .ConfigureAwait(false);
        }

        return await RunAuditWithBarsAsync(auditor, resolved, totalChecks, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<SqlServerAuditRunResult> RunAuditFromSnapshotAsync(
        LogVerbosity verbosity,
        SqlServerAuditor auditor,
        DatabaseSnapshot snapshot,
        EffectiveRunOptions resolved,
        CancellationToken cancellationToken)
    {
        if (verbosity != LogVerbosity.Quiet)
        {
            AnsiConsole.MarkupLine("[grey]      Evaluating health checks against loaded snapshot...[/]");
        }

        return await auditor.RunFromSnapshotAsync(
                snapshot,
                resolved.AuditOptions,
                resolved.Profile,
                resolved.ExcludeSchemas,
                resolved.ExcludeTables,
                checkProgress: null,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<SqlServerAuditRunResult> RunAuditPlainAsync(
        LogVerbosity verbosity,
        SqlServerAuditor auditor,
        EffectiveRunOptions resolved,
        CancellationToken cancellationToken)
    {
        if (verbosity != LogVerbosity.Quiet)
        {
            AnsiConsole.MarkupLine("[grey]      Collecting metadata and evaluating checks...[/]");
        }

        return await auditor.RunWithSnapshotAsync(
                resolved.ConnectionString,
                resolved.AuditOptions,
                resolved.Profile,
                resolved.ExcludeSchemas,
                resolved.ExcludeTables,
                collectionProgress: null,
                checkProgress: null,
                cancellationToken,
                resolved.CommandTimeout)
            .ConfigureAwait(false);
    }

    private static async Task<SqlServerAuditRunResult> RunAuditWithBarsAsync(
        SqlServerAuditor auditor,
        EffectiveRunOptions resolved,
        int totalChecks,
        CancellationToken cancellationToken)
    {
        SqlServerAuditRunResult? result = null;

        await AnsiConsole.Progress()
            .AutoRefresh(enabled: true)
            .AutoClear(enabled: false)
            .HideCompleted(enabled: false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var collectTask = ctx.AddTask(
                    "[cyan]Collecting database snapshot[/]",
                    maxValue: resolved.Profile == AuditProfile.Deep ? 21 : 18);
                var checkTask = ctx.AddTask("[grey]Health checks (waiting...)[/]", maxValue: totalChecks);
                checkTask.Value = 0;

                var collectionProgress = new Progress<CollectionProgress>(cp =>
                {
                    collectTask.MaxValue = cp.Total;
                    collectTask.Value = cp.Completed;
                    collectTask.Description = $"[cyan]{Markup.Escape(cp.StepName)}[/]";
                });

                var checkProgress = new Progress<string>(checkId =>
                {
                    if (collectTask.Value < collectTask.MaxValue)
                    {
                        collectTask.Value = collectTask.MaxValue;
                    }

                    checkTask.Description = $"[deepskyblue1]{Markup.Escape(checkId)}[/]";
                    checkTask.Increment(1);
                });

                result = await auditor.RunWithSnapshotAsync(
                        resolved.ConnectionString,
                        resolved.AuditOptions,
                        resolved.Profile,
                        resolved.ExcludeSchemas,
                        resolved.ExcludeTables,
                        collectionProgress,
                        checkProgress,
                        cancellationToken,
                        resolved.CommandTimeout)
                    .ConfigureAwait(false);

                collectTask.Description = "[green]Database snapshot collected[/]";
                collectTask.Value = collectTask.MaxValue;
                checkTask.Description = "[green]Health checks complete[/]";
                checkTask.Value = checkTask.MaxValue;
            })
            .ConfigureAwait(false);

        return result!;
    }

    public static void PrintCollectionWarnings(LogVerbosity verbosity, IReadOnlyList<CollectionWarning> warnings)
    {
        if (verbosity == LogVerbosity.Quiet || warnings.Count == 0)
        {
            return;
        }

        foreach (var warning in warnings)
        {
            var panel = new Panel($"[grey]{Markup.Escape(warning.Reason)}[/]")
                .Header($"[yellow]⚠  Collection warning: {Markup.Escape(warning.Section)}[/]")
                .BorderColor(Color.Yellow)
                .Padding(1, 0);
            AnsiConsole.Write(panel);
        }
    }

    public static void PrintScanSummary(
        LogVerbosity verbosity,
        EffectiveRunOptions resolved,
        AuditReport report,
        TimeSpan duration)
    {
        if (verbosity == LogVerbosity.Quiet)
        {
            return;
        }

        AnsiConsole.Write(new Rule().RuleStyle("grey"));

        var severityCounts = report.SeverityCounts;
        var maxCount = severityCounts.Count > 0 ? severityCounts.Values.Max() : 1;
        var requiringWindow = report.Findings.Count(f => f.ServiceWindow.RequiresServiceWindow);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[grey]Severity[/]"))
            .AddColumn(new TableColumn("[grey]Count[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]Distribution[/]"));

        foreach (var kvp in severityCounts.OrderBy(k => k.Key))
        {
            var color = SeverityMarkup.GetValueOrDefault(kvp.Key, "white");
            var barLength = maxCount > 0 ? (int)Math.Round(kvp.Value * 20.0 / maxCount) : 0;
            var bar = new string('█', Math.Max(barLength, kvp.Value > 0 ? 1 : 0));
            table.AddRow(
                $"[{color}]{kvp.Key}[/]",
                $"[{color}]{kvp.Value}[/]",
                $"[{color}]{bar}[/]");
        }

        AnsiConsole.Write(table);

        AnsiConsole.MarkupLine($"  [grey]Total findings  :[/] [white]{report.Findings.Count}[/]");
        AnsiConsole.MarkupLine($"  [grey]Requires window :[/] [white]{requiringWindow}[/]");
        AnsiConsole.MarkupLine($"  [grey]Suppressed      :[/] [white]{report.SuppressionSummary.SuppressedFindings}[/]");
        AnsiConsole.MarkupLine($"  [grey]Duration        :[/] [white]{duration:hh\\:mm\\:ss}[/]");

        if (resolved.Format is OutputFormat.Markdown or OutputFormat.Both)
        {
            AnsiConsole.MarkupLine($"  [grey]Markdown report :[/] [cyan]{Markup.Escape(resolved.MarkdownPath)}[/]");
        }

        if (resolved.Format is OutputFormat.Json or OutputFormat.Both)
        {
            AnsiConsole.MarkupLine($"  [grey]JSON report     :[/] [cyan]{Markup.Escape(resolved.JsonPath)}[/]");
        }

        if (resolved.OutputDataModel)
        {
            AnsiConsole.MarkupLine($"  [grey]Data model      :[/] [cyan]{Markup.Escape(resolved.DataModelPath)}[/]");
        }

        AnsiConsole.MarkupLine($"  [grey]SQL scripts     :[/] [cyan]{Markup.Escape(Path.Combine(resolved.FixesDirectory, "no-window"))}[/] [grey](no window)[/]");
        AnsiConsole.MarkupLine($"                     [cyan]{Markup.Escape(Path.Combine(resolved.FixesDirectory, "requires-window"))}[/] [grey](requires window)[/]");

        AnsiConsole.Write(new Rule().RuleStyle("grey"));
    }

    public static void PrintCheckExecutions(
        IReadOnlyList<CheckExecutionResult> executions,
        LogVerbosity verbosity)
    {
        if (executions.Count == 0 || verbosity != LogVerbosity.Verbose)
        {
            return;
        }

        AnsiConsole.Write(new Rule("[grey]Check timings[/]").LeftJustified());

        var table = new Table()
            .Border(TableBorder.Simple)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[grey]Status[/]"))
            .AddColumn(new TableColumn("[grey]Duration[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]Findings[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]Check ID[/]"))
            .AddColumn(new TableColumn("[grey]Title[/]"));

        foreach (var e in executions
            .OrderByDescending(e => e.DurationMs)
            .ThenBy(e => e.CheckId, StringComparer.OrdinalIgnoreCase))
        {
            var (statusText, statusColor) = e.Status switch
            {
                CheckExecutionStatus.Success => ("success", "green"),
                CheckExecutionStatus.Failed => ("failed", "red"),
                _ => ("skipped", "grey"),
            };

            table.AddRow(
                $"[{statusColor}]{statusText}[/]",
                $"[grey]{e.DurationMs}ms[/]",
                $"[white]{e.FindingCount}[/]",
                $"[cyan]{Markup.Escape(e.CheckId)}[/]",
                Markup.Escape(e.Title));
        }

        AnsiConsole.Write(table);
    }
}
