using System;
using System.Collections.Generic;
using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Testing;
using SqlAudit.Cli;
using SqlAudit.Core.Models;
using SqlAudit.SqlServer;
using Xunit;

namespace SqlAudit.Tests;

[Collection("ConsoleTests")]
public sealed class ScanOutputTests : IDisposable
{
    private readonly TestConsole _console;
    private readonly TestConsole _errorConsole;

    public ScanOutputTests()
    {
        _console = new TestConsole();
        _errorConsole = new TestConsole();
        AnsiConsole.Console = _console;
        ScanOutput.ErrorConsole = _errorConsole;
    }

    public void Dispose()
    {
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings()); // Reset
        ScanOutput.ResetErrorConsole();
    }

    [Fact]
    public void PrintBanner_Quiet_DoesNotPrint()
    {
        ScanOutput.PrintBanner(LogVerbosity.Quiet);
        Assert.Empty(_console.Output);
    }

    [Fact]
    public void PrintBanner_Normal_PrintsBanner()
    {
        ScanOutput.PrintBanner(LogVerbosity.Normal);
        Assert.Contains("SqlAudit", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SQL Server Health Audit", _console.Output, StringComparison.OrdinalIgnoreCase);
    }

    private EffectiveRunOptions CreateSampleOptions()
    {
        return new EffectiveRunOptions
        {
            ConnectionString = "Server=localhost;Database=master",
            Profile = AuditProfile.Quick,
            Format = OutputFormat.Both,
            OutputDirectory = "/tmp/out",
            MarkdownPath = "/tmp/out/report.md",
            JsonPath = "/tmp/out/report.json",
            FixesDirectory = "/tmp/out/fixes",
            OutputDataModel = true,
            DataModelPath = "/tmp/out/data.json",
            SuppressionsPath = "/tmp/suppressions.json",
            ExcludeSchemas = new List<string> { "dbo" },
            ExcludeTables = new List<string> { "Users" },
            Verbosity = LogVerbosity.Normal,
            FailOnSeverity = AuditSeverity.High,
            ActiveCheckIds = new List<string>(),
            AuditOptions = new AuditOptions()
        };
    }

    [Fact]
    public void PrintRunConfiguration_Quiet_DoesNotPrint()
    {
        var options = CreateSampleOptions();
        ScanOutput.PrintRunConfiguration(LogVerbosity.Quiet, options, 5);
        Assert.Empty(_console.Output);
    }

    [Fact]
    public void PrintRunConfiguration_Normal_PrintsConfiguration()
    {
        var options = CreateSampleOptions();
        ScanOutput.PrintRunConfiguration(LogVerbosity.Normal, options, 5);

        Assert.Contains("Profile", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quick", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Output format", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("both", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Active checks", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dbo", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Users", _console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartStep_Quiet_ReturnsStopwatch_DoesNotPrint()
    {
        var sw = ScanOutput.StartStep(LogVerbosity.Quiet, 1, 5, "Test Step");

        Assert.NotNull(sw);
        Assert.True(sw.IsRunning);
        Assert.Empty(_console.Output);
    }

    [Fact]
    public void StartStep_Normal_ReturnsStopwatch_PrintsTitle()
    {
        var sw = ScanOutput.StartStep(LogVerbosity.Normal, 2, 10, "Collecting Data");

        Assert.NotNull(sw);
        Assert.True(sw.IsRunning);
        Assert.Contains("[2/10]", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Collecting Data", _console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndStep_Quiet_StopsStopwatch_DoesNotPrint()
    {
        var sw = Stopwatch.StartNew();
        ScanOutput.EndStep(LogVerbosity.Quiet, sw, "Done");

        Assert.False(sw.IsRunning);
        Assert.Empty(_console.Output);
    }

    [Fact]
    public void EndStep_Normal_StopsStopwatch_PrintsMessage()
    {
        var sw = Stopwatch.StartNew();
        ScanOutput.EndStep(LogVerbosity.Normal, sw, "Operation Complete");

        Assert.False(sw.IsRunning);
        Assert.Contains("Operation Complete", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("✓", _console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrintCollectionWarnings_Quiet_DoesNotPrint()
    {
        var warnings = new List<CollectionWarning>
        {
            new CollectionWarning("Query", "Warning message")
        };
        ScanOutput.PrintCollectionWarnings(LogVerbosity.Quiet, warnings);
        Assert.Empty(_console.Output);
    }

    [Fact]
    public void PrintCollectionWarnings_Empty_DoesNotPrint()
    {
        var warnings = new List<CollectionWarning>();
        ScanOutput.PrintCollectionWarnings(LogVerbosity.Normal, warnings);
        Assert.Empty(_console.Output);
    }

    [Fact]
    public void PrintCollectionWarnings_NormalWithWarnings_PrintsWarnings()
    {
        var warnings = new List<CollectionWarning>
        {
            new CollectionWarning("Some Section", "Failed to retrieve data")
        };
        ScanOutput.PrintCollectionWarnings(LogVerbosity.Normal, warnings);

        // Output contains Some Section but Spectre.Console formats it using ansi, we can just check for Reason
        Assert.Contains("Failed to retrieve data", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Collection warning", _console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrintScanSummary_Quiet_DoesNotPrint()
    {
        var options = CreateSampleOptions();
        var report = new AuditReport
        {
            ServerName = "Server",
            DatabaseName = "Database",
            Edition = "Developer",
            ProductVersion = "16.0",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Findings = new List<AuditFinding>(),
            CheckExecutions = new List<CheckExecutionResult>(),
            SuppressionSummary = SuppressionSummary.None
        };
        ScanOutput.PrintScanSummary(LogVerbosity.Quiet, options, report, TimeSpan.FromSeconds(5));

        Assert.Empty(_console.Output);
    }

    [Fact]
    public void PrintScanSummary_Normal_PrintsSummary()
    {
        var options = CreateSampleOptions();
        var report = new AuditReport
        {
            ServerName = "Server",
            DatabaseName = "Database",
            Edition = "Developer",
            ProductVersion = "16.0",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Findings = new List<AuditFinding>(),
            CheckExecutions = new List<CheckExecutionResult>(),
            SuppressionSummary = SuppressionSummary.None
        };
        ScanOutput.PrintScanSummary(LogVerbosity.Normal, options, report, TimeSpan.FromSeconds(5));

        Assert.Contains("Total findings", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Requires window", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Duration", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("00:00:05", _console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrintCheckExecutions_Normal_DoesNotPrint()
    {
        var executions = new List<CheckExecutionResult>
        {
            new CheckExecutionResult("CHK-01", "Test Check", "Cat", CheckExecutionStatus.Success, 10, 0, null)
        };
        ScanOutput.PrintCheckExecutions(executions, LogVerbosity.Normal);

        Assert.Empty(_console.Output);
    }

    [Fact]
    public void PrintCheckExecutions_Verbose_Empty_DoesNotPrint()
    {
        var executions = new List<CheckExecutionResult>();
        ScanOutput.PrintCheckExecutions(executions, LogVerbosity.Verbose);

        Assert.Empty(_console.Output);
    }

    [Fact]
    public void PrintCheckExecutions_Verbose_WithExecutions_PrintsExecutions()
    {
        var executions = new List<CheckExecutionResult>
        {
            new CheckExecutionResult("CHK-01", "Success Check", "Cat", CheckExecutionStatus.Success, 10, 0, null),
            new CheckExecutionResult("CHK-02", "Failed Check", "Cat", CheckExecutionStatus.Failed, 20, 2, "Failed")
        };
        ScanOutput.PrintCheckExecutions(executions, LogVerbosity.Verbose);

        Assert.Contains("Check timings", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CHK-01", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Success Check", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("success", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10ms", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CHK-02", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Failed Check", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("failed", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("20ms", _console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShowError_WithHintAndDetails_WritesToErrorConsole()
    {
        ScanOutput.ShowError("Test error", details: "Some detail", hint: "Try this fix");

        Assert.Contains("Error:", _errorConsole.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Test error", _errorConsole.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Try this fix", _errorConsole.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Details: Some detail", _errorConsole.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_console.Output);
    }

    [Fact]
    public void ShowWarning_WritesToErrorConsole()
    {
        ScanOutput.ShowWarning("Warning: disk almost full", details: "10% remaining");

        Assert.Contains("Warning: disk almost full", _errorConsole.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Details: 10% remaining", _errorConsole.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_console.Output);
    }

    [Fact]
    public void ShowSuccess_And_ShowInfo_WritesToConsole()
    {
        ScanOutput.ShowSuccess("Operation succeeded");
        ScanOutput.ShowInfo("Informational message");

        Assert.Contains("Operation succeeded", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Informational message", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_errorConsole.Output);
    }

    [Fact]
    public void PrintDeletingFixScript_Normal_PrintsMessage()
    {
        ScanOutput.PrintDeletingFixScript(LogVerbosity.Normal, "test-fix.sql");
        Assert.Contains("Deleting existing fix script", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test-fix.sql", _console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrintDeletingFixScript_Quiet_DoesNotPrint()
    {
        ScanOutput.PrintDeletingFixScript(LogVerbosity.Quiet, "test-fix.sql");
        Assert.Empty(_console.Output);
    }

    [Fact]
    public void PrintSuppressionsValidationResult_Valid_PrintsSuccess()
    {
        var result = new SuppressionValidationResult(IsValid: true, RuleCount: 3, Warnings: ["Warning 1"], Errors: []);
        ScanOutput.PrintSuppressionsValidationResult("path/to/suppressions.json", result);

        Assert.Contains("Suppressions file", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path/to/suppressions.json", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Warning 1", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Validation passed", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_errorConsole.Output);
    }

    [Fact]
    public void PrintSuppressionsValidationResult_Invalid_PrintsErrorsToErrorConsole()
    {
        var result = new SuppressionValidationResult(IsValid: false, RuleCount: 0, Warnings: [], Errors: ["Syntax error in JSON"]);
        ScanOutput.PrintSuppressionsValidationResult("path/to/bad.json", result);

        Assert.Contains("Validation failed", _errorConsole.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Syntax error in JSON", _errorConsole.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrintReportDiff_Quiet_DoesNotPrint()
    {
        var diff = new ReportDiffResult([], [], [], []);
        ScanOutput.PrintReportDiff(diff, "prev.json", "curr.json", LogVerbosity.Quiet);

        Assert.Empty(_console.Output);
    }

    [Fact]
    public void PrintReportDiff_Normal_PrintsSummaryAndTopFindings()
    {
        var finding = new AuditFinding
        {
            Id = "IDX-001",
            Title = "Missing Index",
            Category = "Indexes",
            Severity = AuditSeverity.High,
            DatabaseObject = "[dbo].[Users]",
            Description = "desc",
            Impact = "impact",
            Recommendation = "rec",
            ServiceWindow = ServiceWindowAdvisor.No("none"),
        };
        var diff = new ReportDiffResult([finding], [], [], []);
        ScanOutput.PrintReportDiff(diff, "prev.json", "curr.json", LogVerbosity.Normal);

        Assert.Contains("Report diff", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prev.json", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("curr.json", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("New findings", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IDX-001", _console.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrintReportDiff_Verbose_PrintsAllFindingsAndImprovements()
    {
        var finding1 = new AuditFinding
        {
            Id = "IDX-001",
            Title = "Missing Index",
            Category = "Indexes",
            Severity = AuditSeverity.High,
            DatabaseObject = "[dbo].[Users]",
            Description = "desc",
            Impact = "impact",
            Recommendation = "rec",
            ServiceWindow = ServiceWindowAdvisor.No("none"),
        };
        var finding2 = new AuditFinding
        {
            Id = "IDX-002",
            Title = "Duplicate Index",
            Category = "Indexes",
            Severity = AuditSeverity.Medium,
            DatabaseObject = "[dbo].[Orders]",
            Description = "desc",
            Impact = "impact",
            Recommendation = "rec",
            ServiceWindow = ServiceWindowAdvisor.No("none"),
        };
        var diff = new ReportDiffResult(
            [finding1],
            [],
            [(finding2, finding1)],
            [(finding1, finding2)]);

        ScanOutput.PrintReportDiff(diff, "prev.json", "curr.json", LogVerbosity.Verbose);

        Assert.Contains("Report diff", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Regressed severity", _console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Improved severity", _console.Output, StringComparison.OrdinalIgnoreCase);
    }
}
