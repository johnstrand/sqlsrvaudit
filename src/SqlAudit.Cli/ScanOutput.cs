using Spectre.Console;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;
using SqlAudit.SqlServer;
using System.Diagnostics;

namespace SqlAudit.Cli;

internal static class ScanOutput
{
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
                cancellationToken)
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
            .AutoRefresh(true)
            .AutoClear(false)
            .HideCompleted(false)
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
                        cancellationToken,
                        collectionProgress,
                        checkProgress)
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
