using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Testing;
using SqlAudit.Cli;
using SqlAudit.Core.Models;

namespace SqlAudit.Tests;

[Collection("ConsoleTests")]
public sealed class ReportDiffCommandTests : IDisposable
{
    private readonly TestConsole _console;
    private readonly TestConsole _errorConsole;

    public ReportDiffCommandTests()
    {
        _console = new TestConsole();
        _errorConsole = new TestConsole();
        AnsiConsole.Console = _console;
        ScanOutput.ErrorConsole = _errorConsole;
    }

    public void Dispose()
    {
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings());
        ScanOutput.ResetErrorConsole();
    }

    [Fact]
    public void Analyze_DetectsNewFixedAndRegressedFindings()
    {
        var previous = CreateReport(
            CreateFinding("IDX-001-1", "[dbo].[Orders]", AuditSeverity.Medium),
            CreateFinding("FK-002-1", "[dbo].[Items]", AuditSeverity.High));

        var current = CreateReport(
            CreateFinding("IDX-001-1", "[dbo].[Orders]", AuditSeverity.Critical),
            CreateFinding("STAT-002-AUTO-CREATE", "MyDb", AuditSeverity.High));

        var diff = ReportDiffCommand.Analyze(previous, current);

        Assert.Single(diff.NewFindings);
        Assert.Single(diff.FixedFindings);
        Assert.Single(diff.Regressed);
        Assert.Empty(diff.Improved);
    }

    [Fact]
    public void Run_UnknownSubcommand_Returns2()
    {
        var options = CreateDiffOptions(subcommand: "unknown", previousPath: null, currentPath: null);
        var exitCode = ReportDiffCommand.Run(options);

        Assert.Equal(2, exitCode);
        Assert.Contains("Unknown report subcommand", _errorConsole.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Run_MissingReportPaths_Returns2()
    {
        var options = CreateDiffOptions(subcommand: "diff", previousPath: null, currentPath: null);
        var exitCode = ReportDiffCommand.Run(options);

        Assert.Equal(2, exitCode);
        Assert.Contains("Missing required options", _errorConsole.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Run_ValidReports_Quiet_ProducesNoOutput()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SqlAuditDiffTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var p1 = Path.Combine(tempDir, "r1.json");
            var p2 = Path.Combine(tempDir, "r2.json");
            File.WriteAllText(p1, JsonSerializer.Serialize(CreateReport()));
            File.WriteAllText(p2, JsonSerializer.Serialize(CreateReport()));

            var options = CreateDiffOptions(subcommand: "diff", previousPath: p1, currentPath: p2, verbosity: LogVerbosity.Quiet);

            var exitCode = ReportDiffCommand.Run(options);
            Assert.Equal(0, exitCode);
            Assert.Empty(_console.Output);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Run_ValidReports_Normal_PrintsDiff()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SqlAuditDiffTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var p1 = Path.Combine(tempDir, "r1.json");
            var p2 = Path.Combine(tempDir, "r2.json");
            File.WriteAllText(p1, JsonSerializer.Serialize(CreateReport(CreateFinding("F1", "tbl", AuditSeverity.High))));
            File.WriteAllText(p2, JsonSerializer.Serialize(CreateReport(CreateFinding("F2", "tbl", AuditSeverity.Medium))));

            var options = CreateDiffOptions(subcommand: "diff", previousPath: p1, currentPath: p2, verbosity: LogVerbosity.Normal);

            var exitCode = ReportDiffCommand.Run(options);
            Assert.Equal(0, exitCode);
            Assert.Contains("Report diff", _console.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("New findings", _console.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Execute_WithQuiet_SuppressesOutput()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SqlAuditDiffTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var p1 = Path.Combine(tempDir, "r1.json");
            var p2 = Path.Combine(tempDir, "r2.json");
            File.WriteAllText(p1, JsonSerializer.Serialize(CreateReport()));
            File.WriteAllText(p2, JsonSerializer.Serialize(CreateReport()));

            var options = CreateDiffOptions(subcommand: "diff", previousPath: p1, currentPath: p2, verbosity: LogVerbosity.Normal);

            var exitCode = ReportDiffCommand.Execute(options, quiet: true);
            Assert.Equal(0, exitCode);
            Assert.Empty(_console.Output);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Execute_WithVerbose_ShowsVerboseDiff()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SqlAuditDiffTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var p1 = Path.Combine(tempDir, "r1.json");
            var p2 = Path.Combine(tempDir, "r2.json");
            var f1 = CreateFinding("F1", "tbl", AuditSeverity.High);
            var f2 = CreateFinding("F1", "tbl", AuditSeverity.Low);
            File.WriteAllText(p1, JsonSerializer.Serialize(CreateReport(f1)));
            File.WriteAllText(p2, JsonSerializer.Serialize(CreateReport(f2)));

            var options = CreateDiffOptions(subcommand: "diff", previousPath: p1, currentPath: p2, verbosity: LogVerbosity.Normal);

            var exitCode = ReportDiffCommand.Execute(options, verbose: true);
            Assert.Equal(0, exitCode);
            Assert.Contains("Report diff", _console.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Improved severity", _console.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Run_NormalMode_PrintsTopImprovedSeverity()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SqlAuditDiffTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var p1 = Path.Combine(tempDir, "r1.json");
            var p2 = Path.Combine(tempDir, "r2.json");
            var f1 = CreateFinding("F1", "tbl", AuditSeverity.High);
            var f2 = CreateFinding("F1", "tbl", AuditSeverity.Low);
            File.WriteAllText(p1, JsonSerializer.Serialize(CreateReport(f1)));
            File.WriteAllText(p2, JsonSerializer.Serialize(CreateReport(f2)));

            var options = CreateDiffOptions(subcommand: "diff", previousPath: p1, currentPath: p2, verbosity: LogVerbosity.Normal);

            var exitCode = ReportDiffCommand.Run(options);
            Assert.Equal(0, exitCode);
            Assert.Contains("Top improved severity:", _console.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("High -> Low", _console.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Run_FailOnRegression_Returns3_WhenNewFindingOrRegressedExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SqlAuditDiffTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var p1 = Path.Combine(tempDir, "r1.json");
            var p2 = Path.Combine(tempDir, "r2.json");
            File.WriteAllText(p1, JsonSerializer.Serialize(CreateReport()));
            File.WriteAllText(p2, JsonSerializer.Serialize(CreateReport(CreateFinding("F1", "tbl", AuditSeverity.Medium))));

            var options = CreateDiffOptions(
                subcommand: "diff",
                previousPath: p1,
                currentPath: p2,
                failOnRegression: true);

            var exitCode = ReportDiffCommand.Run(options);
            Assert.Equal(3, exitCode);
            Assert.Contains("Regression detected", _errorConsole.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Run_JsonAndMarkdownFormats_RenderValidOutputs()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SqlAuditDiffTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var p1 = Path.Combine(tempDir, "r1.json");
            var p2 = Path.Combine(tempDir, "r2.json");
            File.WriteAllText(p1, JsonSerializer.Serialize(CreateReport(CreateFinding("F1", "tbl", AuditSeverity.Medium))));
            File.WriteAllText(p2, JsonSerializer.Serialize(CreateReport(CreateFinding("F1", "tbl", AuditSeverity.High), CreateFinding("F2", "tbl2", AuditSeverity.Low))));

            var jsonOptions = CreateDiffOptions(
                subcommand: "diff",
                previousPath: p1,
                currentPath: p2,
                diffFormat: ReportDiffFormat.Json);

            var jsonExit = ReportDiffCommand.Run(jsonOptions);
            Assert.Equal(0, jsonExit);
            using var doc = JsonDocument.Parse(_console.Output);
            Assert.Equal(1, doc.RootElement.GetProperty("summary").GetProperty("newCount").GetInt32());
            Assert.Equal(1, doc.RootElement.GetProperty("summary").GetProperty("regressedCount").GetInt32());

            var mdConsole = new TestConsole();
            AnsiConsole.Console = mdConsole;
            var mdOptions = CreateDiffOptions(
                subcommand: "diff",
                previousPath: p1,
                currentPath: p2,
                diffFormat: ReportDiffFormat.Markdown);

            var mdExit = ReportDiffCommand.Run(mdOptions);
            Assert.Equal(0, mdExit);
            Assert.Contains("# Report Diff", mdConsole.Output, StringComparison.Ordinal);
            Assert.Contains("## Regressed Severity", mdConsole.Output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static CliOptions CreateDiffOptions(
        string subcommand,
        string? previousPath,
        string? currentPath,
        LogVerbosity verbosity = LogVerbosity.Normal,
        bool failOnRegression = false,
        ReportDiffFormat diffFormat = ReportDiffFormat.Console)
    {
        return new CliOptions
        {
            Command = "report",
            Subcommand = subcommand,
            PreviousReportPath = previousPath,
            CurrentReportPath = currentPath,
            Verbosity = verbosity,
            FailOnRegression = failOnRegression,
            DiffFormat = diffFormat,
            AuditOptionOverrides = new AuditOptionsOverrides(),
        };
    }

    private static AuditReport CreateReport(params AuditFinding[] findings)
    {
        return new AuditReport
        {
            ServerName = "server",
            DatabaseName = "db",
            Edition = "Developer",
            ProductVersion = "16.0",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Findings = findings,
            CheckExecutions = [],
            SuppressionSummary = SuppressionSummary.None,
        };
    }

    private static AuditFinding CreateFinding(string id, string dbObject, AuditSeverity severity)
    {
        return new AuditFinding
        {
            Id = id,
            Title = "title",
            Category = "cat",
            Severity = severity,
            DatabaseObject = dbObject,
            Description = "desc",
            Impact = "impact",
            Recommendation = "rec",
            ServiceWindow = ServiceWindowAdvisor.No("none"),
        };
    }
}