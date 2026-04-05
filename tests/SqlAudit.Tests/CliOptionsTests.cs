using SqlAudit.Cli;
using SqlAudit.Core.Models;

namespace SqlAudit.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void TryParse_ParsesProfileFormatAndThresholds()
    {
        var args = new[]
        {
            "scan",
            "--profile", "quick",
            "--format", "json",
            "--checks", "PK-001,IDX-001",
            "--config", "project-config/sqlaudit.quick.json",
            "--suppressions", "sqlaudit.suppressions.json",
            "--output-data-model",
            "--fail-on", "high",
            "--verbose",
            "--stats-min-mods", "1234",
        };

        var result = CliOptions.TryParse(args);

        Assert.True(result.Success);
        Assert.NotNull(result.Options);
        Assert.Equal(AuditProfile.Quick, result.Options!.Profile);
        Assert.Equal(OutputFormat.Json, result.Options.OutputFormat);
        Assert.Equal(2, result.Options.ActiveCheckIds!.Count);
        Assert.Equal("project-config/sqlaudit.quick.json", result.Options.ConfigPath);
        Assert.Equal("sqlaudit.suppressions.json", result.Options.SuppressionsPath);
        Assert.True(result.Options.OutputDataModel);
        Assert.Equal(AuditSeverity.High, result.Options.FailOnSeverity);
        Assert.Equal(LogVerbosity.Verbose, result.Options.Verbosity);
        Assert.Equal(1234, result.Options.AuditOptionOverrides.StaleStatsMinModifications);
    }

    [Fact]
    public void TryParse_FailsOnInvalidProfile()
    {
        var args = new[] { "scan", "--profile", "turbo" };

        var result = CliOptions.TryParse(args);

        Assert.False(result.Success);
        Assert.Contains("Invalid profile", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_ParsesInitConfigNonInteractivePreset()
    {
        var args = new[]
        {
            "init-config",
            "--config", "ci.sqlaudit.json",
            "--non-interactive",
            "--preset", "deep-strict",
        };

        var result = CliOptions.TryParse(args);

        Assert.True(result.Success);
        Assert.NotNull(result.Options);
        Assert.True(result.Options!.NonInteractive);
        Assert.Equal(ConfigPreset.DeepStrict, result.Options.Preset);
        Assert.Equal("ci.sqlaudit.json", result.Options.ConfigPath);
    }

    [Fact]
    public void TryParse_ParsesSuppressionsInitSubcommandAndFlags()
    {
        var args = new[]
        {
            "suppressions",
            "init",
            "--path", "custom.suppressions.json",
            "--force",
        };

        var result = CliOptions.TryParse(args);

        Assert.True(result.Success);
        Assert.NotNull(result.Options);
        Assert.Equal("suppressions", result.Options!.Command);
        Assert.Equal("init", result.Options.Subcommand);
        Assert.Equal("custom.suppressions.json", result.Options.SuppressionsPath);
        Assert.True(result.Options.Force);
    }

    [Fact]
    public void TryParse_FailsWhenSuppressionsSubcommandMissing()
    {
        var args = new[] { "suppressions", "--path", "a.json" };

        var result = CliOptions.TryParse(args);

        Assert.False(result.Success);
        Assert.Contains("Missing suppressions subcommand", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_ParsesReportDiffArguments()
    {
        var args = new[]
        {
            "report",
            "diff",
            "--previous", "old.json",
            "--current", "new.json",
            "--quiet",
        };

        var result = CliOptions.TryParse(args);

        Assert.True(result.Success);
        Assert.NotNull(result.Options);
        Assert.Equal("report", result.Options!.Command);
        Assert.Equal("diff", result.Options.Subcommand);
        Assert.Equal("old.json", result.Options.PreviousReportPath);
        Assert.Equal("new.json", result.Options.CurrentReportPath);
        Assert.Equal(LogVerbosity.Quiet, result.Options.Verbosity);
    }
}
