using SqlAudit.Cli;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;
using SqlAudit.Reporting;
using SqlAudit.SqlServer;
using System.Data.Common;
using System.Diagnostics;

int exitCode;
try
{
    exitCode = await RunAsync(args).ConfigureAwait(false);
}
catch (Exception ex)
{
    exitCode = HandleUnhandledException(ex);
}

Environment.ExitCode = exitCode;

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0)
    {
        CliOptions.PrintHelp();
        return 0;
    }

    var parseResult = CliOptions.TryParse(args);
    if (!parseResult.Success)
    {
        if (parseResult.ErrorMessage is null)
        {
            return 0;
        }

        await Console.Error.WriteLineAsync(parseResult.ErrorMessage).ConfigureAwait(false);
        await Console.Error.WriteLineAsync().ConfigureAwait(false);
        CliOptions.PrintHelp();
        return 2;
    }

    var options = parseResult.Options!;

    if (string.Equals(options.Command, "init-config", StringComparison.OrdinalIgnoreCase))
    {
        return InteractiveConfigWizard.Run(options);
    }

    if (string.Equals(options.Command, "suppressions", StringComparison.OrdinalIgnoreCase))
    {
        return SuppressionsCommand.Run(options);
    }

    if (string.Equals(options.Command, "report", StringComparison.OrdinalIgnoreCase))
    {
        return ReportDiffCommand.Run(options);
    }

    if (!string.Equals(options.Command, "scan", StringComparison.OrdinalIgnoreCase))
    {
        await Console.Error.WriteLineAsync($"Unknown command: {options.Command}").ConfigureAwait(false);
        CliOptions.PrintHelp();
        return 2;
    }

    EffectiveRunOptions resolved;
    try
    {
        var stepResolve = StartStep(LogVerbosity.Normal, 1, 6, "Resolve configuration");
        resolved = ProjectConfigurationResolver.Resolve(options, Environment.GetEnvironmentVariable("SQLAUDIT_CONNECTION"));
        EndStep(LogVerbosity.Normal, stepResolve, "Configuration resolved");
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
        return 2;
    }

    var verbosity = resolved.Verbosity;
    PrintBanner(verbosity);

    var runTimer = Stopwatch.StartNew();
    var checks = SqlServerHealthChecks.Create(resolved.Profile, resolved.ActiveCheckIds);

    PrintLine(verbosity, LogVerbosity.Normal, $"  Profile      : {resolved.Profile.ToString().ToLowerInvariant()}");
    PrintLine(verbosity, LogVerbosity.Normal, $"  Output format: {resolved.Format.ToString().ToLowerInvariant()}");
    PrintLine(verbosity, LogVerbosity.Normal, $"  Active checks: {checks.Count}");
    PrintLine(verbosity, LogVerbosity.Normal, $"  Output dir   : {resolved.OutputDirectory}");
    PrintLine(verbosity, LogVerbosity.Normal, $"  Suppressions : {(resolved.SuppressionsPath ?? "(none)")}");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cts.Cancel();
    };

    var stepPreflight = StartStep(verbosity, 2, 6, "Run connection preflight checks");
    var preflight = await SqlServerPreflight.RunAsync(resolved.ConnectionString, cts.Token).ConfigureAwait(false);
    EndStep(verbosity, stepPreflight, $"Connected to {preflight.ServerName} / {preflight.DatabaseName}");

    var stepAudit = StartStep(verbosity, 3, 6, "Run SQL Server analysis");
    PrintLine(verbosity, LogVerbosity.Normal, "      Collecting metadata and evaluating checks...");
    var auditor = new SqlServerAuditor(checks);
    var report = await auditor.RunAsync(resolved.ConnectionString, resolved.AuditOptions, resolved.Profile, cts.Token).ConfigureAwait(false);
    EndStep(verbosity, stepAudit, $"Analysis complete ({report.Findings.Count} findings)");

    var stepSuppressions = StartStep(verbosity, 4, 6, "Apply suppressions");
    var suppressionRules = SuppressionFileLoader.Load(resolved.SuppressionsPath);
    var suppressionOutcome = AuditFindingSuppressor.Apply(report.Findings, suppressionRules, DateTimeOffset.UtcNow);
    report = ApplySuppressionResult(report, suppressionOutcome);
    EndStep(verbosity, stepSuppressions, $"Suppressed {suppressionOutcome.Summary.SuppressedFindings} findings using {suppressionOutcome.Summary.ActiveRules} active rules");

    var stepReports = StartStep(verbosity, 5, 6, "Render report files");
    Directory.CreateDirectory(resolved.OutputDirectory);
    Directory.CreateDirectory(Path.GetDirectoryName(resolved.MarkdownPath) ?? resolved.OutputDirectory);
    Directory.CreateDirectory(Path.GetDirectoryName(resolved.JsonPath) ?? resolved.OutputDirectory);
    Directory.CreateDirectory(resolved.FixesDirectory);

    if (resolved.Format is OutputFormat.Markdown or OutputFormat.Both)
    {
        var markdown = MarkdownReportRenderer.Render(report);
        await File.WriteAllTextAsync(resolved.MarkdownPath, markdown, cts.Token).ConfigureAwait(false);
    }

    if (resolved.Format is OutputFormat.Json or OutputFormat.Both)
    {
        var json = JsonReportRenderer.Render(report);
        await File.WriteAllTextAsync(resolved.JsonPath, json, cts.Token).ConfigureAwait(false);
    }
    EndStep(verbosity, stepReports, "Report files written");

    var stepScripts = StartStep(verbosity, 6, 6, "Generate SQL remediation scripts");
    var scripts = SqlFixScriptRenderer.Render(report);
    var combinedPath = Path.Combine(resolved.FixesDirectory, "all-fixes.sql");
    await File.WriteAllTextAsync(combinedPath, scripts.CombinedScript, cts.Token).ConfigureAwait(false);

    foreach (var script in scripts.IndividualScripts)
    {
        await File.WriteAllTextAsync(Path.Combine(resolved.FixesDirectory, script.Key), script.Value, cts.Token).ConfigureAwait(false);
    }
    EndStep(verbosity, stepScripts, $"Script bundle written ({scripts.IndividualScripts.Count} individual scripts)");

    var requiringWindow = report.Findings.Count(f => f.ServiceWindow.RequiresServiceWindow);
    var noWindow = report.Findings.Count - requiringWindow;

    PrintSeparator(verbosity);
    WriteLabel("Scan completed", ConsoleColor.Green);
    Console.WriteLine();
    Console.WriteLine($"  Total findings      : {report.Findings.Count}");
    Console.WriteLine($"  Service window yes  : {requiringWindow}");
    Console.WriteLine($"  Service window no   : {noWindow}");
    foreach (var severity in report.SeverityCounts.OrderBy(kvp => kvp.Key))
    {
        Console.WriteLine($"  Severity {severity.Key,-9}: {severity.Value}");
    }
    foreach (var category in report.CategoryCounts.OrderByDescending(kvp => kvp.Value).ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine($"  Category {category.Key,-9}: {category.Value}");
    }
    Console.WriteLine($"  Suppressed findings : {report.SuppressionSummary.SuppressedFindings}");
    Console.WriteLine($"  Suppression rules   : {report.SuppressionSummary.ActiveRules} active, {report.SuppressionSummary.ExpiredRules} expired");
    Console.WriteLine($"  Duration            : {runTimer.Elapsed:hh\\:mm\\:ss}");
    PrintSeparator(verbosity);

    if (verbosity == LogVerbosity.Verbose)
    {
        PrintCheckExecutions(report.CheckExecutions, verbosity);
    }

    if (resolved.Format is OutputFormat.Markdown or OutputFormat.Both)
    {
        Console.WriteLine($"  Markdown report : {resolved.MarkdownPath}");
    }

    if (resolved.Format is OutputFormat.Json or OutputFormat.Both)
    {
        Console.WriteLine($"  JSON report     : {resolved.JsonPath}");
    }

    Console.WriteLine($"  SQL scripts     : {resolved.FixesDirectory}");

    if (IsFailThresholdBreached(report, resolved.FailOnSeverity, out var threshold, out var matchingCount))
    {
        await Console.Error.WriteLineAsync($"Fail-on threshold hit: {threshold} ({matchingCount} finding(s) at or above threshold).").ConfigureAwait(false);
        return 3;
    }

    return 0;
}

static bool IsFailThresholdBreached(AuditReport report, AuditSeverity? threshold, out AuditSeverity actualThreshold, out int matchingCount)
{
    if (!threshold.HasValue)
    {
        actualThreshold = AuditSeverity.Info;
        matchingCount = 0;
        return false;
    }

    actualThreshold = threshold.Value;
    matchingCount = report.Findings.Count(f => f.Severity <= threshold.Value);
    return matchingCount > 0;
}

static AuditReport ApplySuppressionResult(AuditReport source, SuppressionOutcome suppression)
{
    return new AuditReport
    {
        SchemaVersion = source.SchemaVersion,
        ServerName = source.ServerName,
        DatabaseName = source.DatabaseName,
        Edition = source.Edition,
        ProductVersion = source.ProductVersion,
        CapturedAtUtc = source.CapturedAtUtc,
        Findings = suppression.Findings,
        CheckExecutions = source.CheckExecutions,
        SuppressionSummary = suppression.Summary
    };
}

static void PrintCheckExecutions(IReadOnlyList<CheckExecutionResult> executions, LogVerbosity verbosity)
{
    if (executions.Count == 0)
    {
        return;
    }

    PrintSeparator(verbosity);
    WriteLabel("Check timings", ConsoleColor.Cyan);
    Console.WriteLine();
    Console.WriteLine("  Status   Duration   Findings  CheckId     Title");
    Console.WriteLine("  -------  ---------  --------  ----------  -----------------------------");

    foreach (var execution in executions.OrderByDescending(e => e.DurationMs).ThenBy(e => e.CheckId, StringComparer.OrdinalIgnoreCase))
    {
        var statusText = execution.Status switch
        {
            CheckExecutionStatus.Success => "success",
            CheckExecutionStatus.Failed => "failed",
            _ => "skipped"
        };

        Console.WriteLine($"  {statusText,-7}  {execution.DurationMs,7}ms  {execution.FindingCount,8}  {execution.CheckId,-10}  {execution.Title}");
    }
}

static Stopwatch StartStep(LogVerbosity verbosity, int current, int total, string title)
{
    if (verbosity != LogVerbosity.Quiet)
    {
        WriteLabel($"[{current}/{total}]", ConsoleColor.Cyan);
        Console.WriteLine($" {title}");
    }

    return Stopwatch.StartNew();
}

static void EndStep(LogVerbosity verbosity, Stopwatch stopwatch, string message)
{
    stopwatch.Stop();
    if (verbosity != LogVerbosity.Quiet)
    {
        WriteLabel("  [OK]", ConsoleColor.Green);
        Console.WriteLine($" {message} ({stopwatch.Elapsed:hh\\:mm\\:ss})");
    }
}

static void PrintBanner(LogVerbosity verbosity)
{
    if (verbosity == LogVerbosity.Quiet)
    {
        return;
    }

    PrintSeparator(verbosity);
    WriteLabel("SqlAudit", ConsoleColor.Cyan);
    Console.WriteLine(" - SQL Server Health Audit");
    PrintSeparator(verbosity);
}

static void PrintSeparator(LogVerbosity verbosity)
{
    if (verbosity != LogVerbosity.Quiet)
    {
        Console.WriteLine("------------------------------------------------------------");
    }
}

static void PrintLine(LogVerbosity current, LogVerbosity minimum, string text)
{
    if (current >= minimum)
    {
        Console.WriteLine(text);
    }
}

static void WriteLabel(string text, ConsoleColor color)
{
    var previous = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.Write(text);
    Console.ForegroundColor = previous;
}

static int HandleUnhandledException(Exception ex)
{
    if (ex is OperationCanceledException)
    {
        Console.Error.WriteLine("Operation cancelled.");
        return 130;
    }

    if (ex is DbException dbException)
    {
        return HandleDatabaseException(dbException);
    }

    if (ex is FileNotFoundException fileMissing)
    {
        Console.Error.WriteLine("Required file was not found.");
        Console.Error.WriteLine($"Details: {fileMissing.Message}");
        return 1;
    }

    if (ex is IOException ioException)
    {
        Console.Error.WriteLine("I/O error while reading or writing files.");
        Console.Error.WriteLine($"Details: {ioException.Message}");
        return 1;
    }

    if (ex is UnauthorizedAccessException unauthorized)
    {
        Console.Error.WriteLine("Permission error while accessing files or directories.");
        Console.Error.WriteLine($"Details: {unauthorized.Message}");
        return 1;
    }

    Console.Error.WriteLine("Unexpected error.");
    Console.Error.WriteLine($"Details: {ex.Message}");
    return 1;
}

static int HandleDatabaseException(DbException ex)
{
    var message = ex.Message ?? string.Empty;
    if (message.Contains("login failed", StringComparison.OrdinalIgnoreCase)
        || message.Contains("cannot open database", StringComparison.OrdinalIgnoreCase)
        || message.Contains("password", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Database authentication failed.");
        Console.Error.WriteLine("Check connection string values for server, username, password, and database.");
        Console.Error.WriteLine($"Details: {message}");
        return 1;
    }

    if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
        || message.Contains("network-related", StringComparison.OrdinalIgnoreCase)
        || message.Contains("server was not found", StringComparison.OrdinalIgnoreCase)
        || message.Contains("could not open a connection", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Could not connect to SQL Server.");
        Console.Error.WriteLine("Check that SQL Server is running, reachable, and the port is correct.");
        Console.Error.WriteLine($"Details: {message}");
        return 1;
    }

    Console.Error.WriteLine("Database operation failed.");
    Console.Error.WriteLine($"Details: {message}");
    return 1;
}
