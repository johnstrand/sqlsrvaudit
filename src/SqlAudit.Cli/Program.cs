using Spectre.Console;
using SqlAudit.Cli;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;
using SqlAudit.Reporting;
using SqlAudit.SqlServer;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;

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
        return await HandleParseFailureAsync(parseResult).ConfigureAwait(false);
    }

    return await DispatchCommandAsync(parseResult.Options!).ConfigureAwait(false);
}

static async Task<int> HandleParseFailureAsync(ParseResult parseResult)
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

static Task<int> DispatchCommandAsync(CliOptions options)
{
    if (IsCommand(options.Command, "init-config"))
    {
        return Task.FromResult(InteractiveConfigWizard.Run(options));
    }

    if (IsCommand(options.Command, "suppressions"))
    {
        return Task.FromResult(SuppressionsCommand.Run(options));
    }

    if (IsCommand(options.Command, "report"))
    {
        return Task.FromResult(ReportDiffCommand.Run(options));
    }

    if (!IsCommand(options.Command, "scan"))
    {
        return HandleUnknownCommandAsync(options.Command);
    }

    return RunScanCommandAsync(options);
}

static bool IsCommand(string actual, string expected) =>
    string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

static async Task<int> HandleUnknownCommandAsync(string command)
{
    await Console.Error.WriteLineAsync($"Unknown command: {command}").ConfigureAwait(false);
    CliOptions.PrintHelp();
    return 2;
}

static async Task<int> RunScanCommandAsync(CliOptions options)
{
    var resolveResult = await ResolveRunOptionsAsync(options).ConfigureAwait(false);
    if (!resolveResult.Success)
    {
        return resolveResult.ExitCode;
    }

    var resolved = resolveResult.Options!;
    var verbosity = resolved.Verbosity;
    var runTimer = Stopwatch.StartNew();
    var checks = SqlServerHealthChecks.Create(resolved.Profile, resolved.ActiveCheckIds);

    ScanOutput.PrintBanner(verbosity);
    ScanOutput.PrintRunConfiguration(verbosity, resolved, checks.Count);

    using var cts = CreateCancellationTokenSource();
    await RunPreflightAsync(verbosity, resolved, cts.Token).ConfigureAwait(false);

    var auditRun = await RunAuditAsync(verbosity, resolved, checks, cts.Token).ConfigureAwait(false);
    ScanOutput.PrintCollectionWarnings(verbosity, auditRun.Snapshot.CollectionWarnings);
    var reportWithForecasts = AttachGrowthForecasts(auditRun.Report, resolved.DataModelPath, auditRun.Snapshot);
    var report = ApplySuppressions(verbosity, reportWithForecasts, resolved.SuppressionsPath);

    await WriteOutputsAsync(verbosity, resolved, report, auditRun.Snapshot, cts.Token).ConfigureAwait(false);
    ScanOutput.PrintScanSummary(verbosity, resolved, report, runTimer.Elapsed);
    ScanOutput.PrintCheckExecutions(report.CheckExecutions, verbosity);

    if (IsFailThresholdBreached(report, resolved.FailOnSeverity, out var threshold, out var matchingCount))
    {
        await Console.Error.WriteLineAsync($"Fail-on threshold hit: {threshold} ({matchingCount} finding(s) at or above threshold).")
            .ConfigureAwait(false);
        return 3;
    }

    return 0;
}

static async Task<ResolveRunOptionsResult> ResolveRunOptionsAsync(CliOptions options)
{
    try
    {
        var stepResolve = ScanOutput.StartStep(LogVerbosity.Normal, 1, 6, "Resolve configuration");
        var resolved = ProjectConfigurationResolver.Resolve(options, Environment.GetEnvironmentVariable("SQLAUDIT_CONNECTION"));
        ScanOutput.EndStep(LogVerbosity.Normal, stepResolve, "Configuration resolved");
        return ResolveRunOptionsResult.Ok(resolved);
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
        return ResolveRunOptionsResult.Fail(2);
    }
}

static CancellationTokenSource CreateCancellationTokenSource()
{
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cts.Cancel();
    };

    return cts;
}

static async Task RunPreflightAsync(LogVerbosity verbosity, EffectiveRunOptions resolved, CancellationToken cancellationToken)
{
    var stepPreflight = ScanOutput.StartStep(verbosity, 2, 6, "Run connection preflight checks");
    var preflight = await SqlServerPreflight.RunAsync(resolved.ConnectionString, cancellationToken, resolved.CommandTimeout).ConfigureAwait(false);
    ScanOutput.EndStep(verbosity, stepPreflight, $"Connected to {preflight.ServerName} / {preflight.DatabaseName}");
}

static async Task<SqlServerAuditRunResult> RunAuditAsync(
    LogVerbosity verbosity,
    EffectiveRunOptions resolved,
    IReadOnlyCollection<SqlAudit.Core.Abstractions.IHealthCheck> checks,
    CancellationToken cancellationToken)
{
    var stepAudit = ScanOutput.StartStep(verbosity, 3, 6, "Run SQL Server analysis");
    var auditor = new SqlServerAuditor(checks);
    var run = await ScanOutput.RunAuditWithProgressAsync(
            verbosity, auditor, resolved, checks.Count, cancellationToken)
        .ConfigureAwait(false);
    ScanOutput.EndStep(verbosity, stepAudit, $"Analysis complete — {run.Report.Findings.Count} findings");
    return run;
}

static AuditReport ApplySuppressions(LogVerbosity verbosity, AuditReport report, string? suppressionsPath)
{
    var stepSuppressions = ScanOutput.StartStep(verbosity, 4, 6, "Apply suppressions");
    var suppressionRules = SuppressionFileLoader.Load(suppressionsPath);
    var suppressionOutcome = AuditFindingSuppressor.Apply(report.Findings, suppressionRules, DateTimeOffset.UtcNow);
    var updatedReport = ApplySuppressionResult(report, suppressionOutcome);

    ScanOutput.EndStep(
        verbosity,
        stepSuppressions,
        $"Suppressed {suppressionOutcome.Summary.SuppressedFindings} findings using {suppressionOutcome.Summary.ActiveRules} active rules");

    return updatedReport;
}

static async Task WriteOutputsAsync(
    LogVerbosity verbosity,
    EffectiveRunOptions resolved,
    AuditReport report,
    DatabaseSnapshot snapshot,
    CancellationToken cancellationToken)
{
    var stepReports = ScanOutput.StartStep(verbosity, 5, 6, "Render report files");
    EnsureOutputDirectoriesExist(resolved);

    if (resolved.Format is OutputFormat.Markdown or OutputFormat.Both)
    {
        var markdown = MarkdownReportRenderer.Render(report);
        await File.WriteAllTextAsync(resolved.MarkdownPath, markdown, cancellationToken).ConfigureAwait(false);
    }

    if (resolved.Format is OutputFormat.Json or OutputFormat.Both)
    {
        var json = JsonReportRenderer.Render(report);
        await File.WriteAllTextAsync(resolved.JsonPath, json, cancellationToken).ConfigureAwait(false);
    }

    if (resolved.OutputDataModel)
    {
        var dataModelJson = DataModelJsonRenderer.Render(snapshot);
        await File.WriteAllTextAsync(resolved.DataModelPath, dataModelJson, cancellationToken).ConfigureAwait(false);
    }

    ScanOutput.EndStep(verbosity, stepReports, "Report files written");

    var stepScripts = ScanOutput.StartStep(verbosity, 6, 6, "Generate SQL remediation scripts");
    CleanFixesDirectory(resolved.FixesDirectory, verbosity);
    var scripts = SqlFixScriptRenderer.Render(report);
    var combinedPath = Path.Combine(resolved.FixesDirectory, "all-fixes.sql");
    await File.WriteAllTextAsync(combinedPath, scripts.CombinedScript, cancellationToken).ConfigureAwait(false);

    var noWindowDir = Path.Combine(resolved.FixesDirectory, "no-window");
    var requiresWindowDir = Path.Combine(resolved.FixesDirectory, "requires-window");
    Directory.CreateDirectory(noWindowDir);
    Directory.CreateDirectory(requiresWindowDir);

    foreach (var script in scripts.NoWindowScripts)
    {
        await File.WriteAllTextAsync(Path.Combine(noWindowDir, script.Key), script.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    foreach (var script in scripts.RequiresWindowScripts)
    {
        await File.WriteAllTextAsync(Path.Combine(requiresWindowDir, script.Key), script.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    ScanOutput.EndStep(verbosity, stepScripts, $"Script bundle written ({scripts.NoWindowScripts.Count} no-window, {scripts.RequiresWindowScripts.Count} requires-window)");
}

static void EnsureOutputDirectoriesExist(EffectiveRunOptions resolved)
{
    Directory.CreateDirectory(resolved.OutputDirectory);
    Directory.CreateDirectory(Path.GetDirectoryName(resolved.MarkdownPath) ?? resolved.OutputDirectory);
    Directory.CreateDirectory(Path.GetDirectoryName(resolved.JsonPath) ?? resolved.OutputDirectory);
    Directory.CreateDirectory(Path.GetDirectoryName(resolved.DataModelPath) ?? resolved.OutputDirectory);
    Directory.CreateDirectory(resolved.FixesDirectory);
}

static void CleanFixesDirectory(string fixesDirectory, LogVerbosity verbosity)
{
    foreach (var file in Directory.EnumerateFiles(fixesDirectory, "*.sql", SearchOption.AllDirectories))
    {
        if (verbosity >= LogVerbosity.Normal)
        {
            AnsiConsole.MarkupLine($"[grey]Deleting existing fix script:[/] [green]{file}[/]");
        }

        File.Delete(file);
    }
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
        ExcludedSchemas = source.ExcludedSchemas,
        ExcludedTables = source.ExcludedTables,
        TopResourceIntensiveQueries = source.TopResourceIntensiveQueries,
        TopWaitStats = source.TopWaitStats,
        QueryStoreRegressions = source.QueryStoreRegressions,
        ActiveBlockingSessions = source.ActiveBlockingSessions,
        DeadlockSummary = source.DeadlockSummary,
        MissingIndexSignals = source.MissingIndexSignals,
        LogHealth = source.LogHealth,
        TempDbPressure = source.TempDbPressure,
        FileGrowthHealth = source.FileGrowthHealth,
        BackupPosture = source.BackupPosture,
        SecurityHygieneIssues = source.SecurityHygieneIssues,
        TableGrowthForecasts = source.TableGrowthForecasts,
        CollectionWarnings = source.CollectionWarnings,
        ServerConfigurations = source.ServerConfigurations,
        LastDbccCheckDbUtc = source.LastDbccCheckDbUtc,
        TempDbConfig = source.TempDbConfig,
        SleepingTransactions = source.SleepingTransactions,
        MemoryPressure = source.MemoryPressure,
        FileIoLatency = source.FileIoLatency,
        PlanCache = source.PlanCache,
        TableCompression = source.TableCompression,
        DatabaseOptions = source.DatabaseOptions,
        VolumeStats = source.VolumeStats,
        FailedAgentJobs = source.FailedAgentJobs,
        GlobalTraceFlags = source.GlobalTraceFlags,
        Findings = suppression.Findings,
        CheckExecutions = source.CheckExecutions,
        SuppressionSummary = suppression.Summary,
    };
}

static AuditReport AttachGrowthForecasts(AuditReport report, string dataModelPath, DatabaseSnapshot currentSnapshot)
{
    var previousSnapshot = TryLoadSnapshot(dataModelPath);
    if (previousSnapshot is null)
    {
        return report;
    }

    var forecasts = BuildGrowthForecasts(previousSnapshot, currentSnapshot);
    if (forecasts.Count == 0)
    {
        return report;
    }

    return new AuditReport
    {
        SchemaVersion = report.SchemaVersion,
        ServerName = report.ServerName,
        DatabaseName = report.DatabaseName,
        Edition = report.Edition,
        ProductVersion = report.ProductVersion,
        CapturedAtUtc = report.CapturedAtUtc,
        ExcludedSchemas = report.ExcludedSchemas,
        ExcludedTables = report.ExcludedTables,
        TopResourceIntensiveQueries = report.TopResourceIntensiveQueries,
        TopWaitStats = report.TopWaitStats,
        QueryStoreRegressions = report.QueryStoreRegressions,
        ActiveBlockingSessions = report.ActiveBlockingSessions,
        DeadlockSummary = report.DeadlockSummary,
        MissingIndexSignals = report.MissingIndexSignals,
        LogHealth = report.LogHealth,
        TempDbPressure = report.TempDbPressure,
        FileGrowthHealth = report.FileGrowthHealth,
        BackupPosture = report.BackupPosture,
        SecurityHygieneIssues = report.SecurityHygieneIssues,
        TableGrowthForecasts = forecasts,
        CollectionWarnings = report.CollectionWarnings,
        ServerConfigurations = report.ServerConfigurations,
        LastDbccCheckDbUtc = report.LastDbccCheckDbUtc,
        TempDbConfig = report.TempDbConfig,
        SleepingTransactions = report.SleepingTransactions,
        MemoryPressure = report.MemoryPressure,
        FileIoLatency = report.FileIoLatency,
        PlanCache = report.PlanCache,
        TableCompression = report.TableCompression,
        DatabaseOptions = report.DatabaseOptions,
        VolumeStats = report.VolumeStats,
        FailedAgentJobs = report.FailedAgentJobs,
        GlobalTraceFlags = report.GlobalTraceFlags,
        Findings = report.Findings,
        CheckExecutions = report.CheckExecutions,
        SuppressionSummary = report.SuppressionSummary,
    };
}

static DatabaseSnapshot? TryLoadSnapshot(string dataModelPath)
{
    if (!File.Exists(dataModelPath))
    {
        return null;
    }

    try
    {
        var json = File.ReadAllText(dataModelPath);
        return JsonSerializer.Deserialize<DatabaseSnapshot>(json);
    }
    catch (Exception)
    {
        return null;
    }
}

static IReadOnlyList<TableGrowthForecastInfo> BuildGrowthForecasts(DatabaseSnapshot previous, DatabaseSnapshot current)
{
    var previousCaptured = previous.CapturedAtUtc;
    if (previousCaptured == default)
    {
        return [];
    }

    var elapsedDays = Math.Max(0, (current.CapturedAtUtc - previousCaptured).TotalDays);
    if (elapsedDays < 1)
    {
        return [];
    }

    var previousByTable = previous.Tables
        .ToDictionary(
            table => $"{table.SchemaName}.{table.TableName}",
            table => table,
            StringComparer.OrdinalIgnoreCase);

    var forecasts = current.Tables
        .Where(table => previousByTable.ContainsKey($"{table.SchemaName}.{table.TableName}"))
        .Select(table =>
        {
            var key = $"{table.SchemaName}.{table.TableName}";
            var previousTable = previousByTable[key];
            var previousMb = previousTable.ReservedMb;
            var currentMb = table.ReservedMb;
            var deltaMb = currentMb - previousMb;
            if (deltaMb <= 10m)
            {
                return null;
            }

            var growthPerDay = deltaMb / Convert.ToDecimal(elapsedDays, System.Globalization.CultureInfo.InvariantCulture);
            return new TableGrowthForecastInfo(
                $"[{table.SchemaName}].[{table.TableName}]",
                previousMb,
                currentMb,
                deltaMb,
                Convert.ToDecimal(elapsedDays, System.Globalization.CultureInfo.InvariantCulture),
                currentMb + (growthPerDay * 30m),
                currentMb + (growthPerDay * 90m));
        })
        .Where(forecast => forecast is not null)
        .Select(forecast => forecast!)
        .OrderByDescending(forecast => forecast.DeltaReservedMb)
        .Take(15)
        .ToArray();

    return forecasts;
}

static int HandleUnhandledException(Exception ex)
{
    return ex switch
    {
        OperationCanceledException => PrintError("Operation cancelled.", details: null, exitCode: 130),
        DbException dbException => HandleDatabaseException(dbException),
        FileNotFoundException fileMissing => PrintError("Required file was not found.", fileMissing.Message, exitCode: 1),
        IOException ioException => PrintError("I/O error while reading or writing files.", ioException.Message, exitCode: 1),
        UnauthorizedAccessException unauthorized => PrintError("Permission error while accessing files or directories.", unauthorized.Message, exitCode: 1),
        _ => PrintError("Unexpected error.", ex.Message, exitCode: 1),
    };
}

static int PrintError(string message, string? details, int exitCode)
{
    Console.Error.WriteLine(message);
    if (!string.IsNullOrWhiteSpace(details))
    {
        Console.Error.WriteLine($"Details: {details}");
    }

    return exitCode;
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
