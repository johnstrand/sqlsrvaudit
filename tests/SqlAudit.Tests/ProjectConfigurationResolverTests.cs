using System.Text.Json;
using SqlAudit.Cli;
using SqlAudit.Core.Models;

namespace SqlAudit.Tests;

public sealed class ProjectConfigurationResolverTests
{
    [Fact]
    public void Resolve_MergesProfileConfigAndCliOverrides()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SqlAuditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var configPath = Path.Combine(tempDir, "sqlaudit.project.json");
            var configJson = JsonSerializer.Serialize(new
            {
                profile = "quick",
                outputFormat = "json",
                outputDirectory = "./out-from-config",
                auditOptions = new
                {
                    staleStatsMinModifications = 222,
                    identityUsageWarningPercent = 91,
                },
            });

            File.WriteAllText(configPath, configJson);

            var cli = new CliOptions
            {
                Command = "scan",
                ConnectionString = "Server=.;Database=Db;Trusted_Connection=True;",
                ConfigPath = configPath,
                OutputDirectory = null,
                MarkdownPath = null,
                JsonPath = null,
                FixesDirectory = null,
                Profile = null,
                OutputFormat = OutputFormat.Both,
                AuditOptionOverrides = new AuditOptionsOverrides
                {
                    StaleStatsMinModifications = 999,
                },
            };

            var resolved = ProjectConfigurationResolver.Resolve(cli, environmentConnectionString: null);

            Assert.Equal(AuditProfile.Quick, resolved.Profile);
            Assert.Equal(OutputFormat.Both, resolved.Format);
            Assert.Equal(999, resolved.AuditOptions.StaleStatsMinModifications);
            Assert.Equal(91, resolved.AuditOptions.IdentityUsageWarningPercent);
            Assert.Contains("out-from-config", resolved.OutputDirectory, StringComparison.OrdinalIgnoreCase);
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
    public void Resolve_ThrowsForInvalidCheckIdForProfile()
    {
        var cli = new CliOptions
        {
            Command = "scan",
            ConnectionString = "Server=.;Database=Db;Trusted_Connection=True;",
            ConfigPath = null,
            SuppressionsPath = null,
            OutputDirectory = null,
            MarkdownPath = null,
            JsonPath = null,
            FixesDirectory = null,
            Profile = AuditProfile.Quick,
            OutputFormat = OutputFormat.Both,
            ActiveCheckIds = ["STAT-001"],
            AuditOptionOverrides = new AuditOptionsOverrides(),
        };

        var exception = Assert.Throws<InvalidOperationException>(() => ProjectConfigurationResolver.Resolve(cli, environmentConnectionString: null));
        Assert.Contains("Invalid check id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_UsesSuppressionsPathFromConfigWhenPresent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SqlAuditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var configPath = Path.Combine(tempDir, "sqlaudit.project.json");
            var suppressionsPath = Path.Combine(tempDir, "sqlaudit.suppressions.json");
            File.WriteAllText(suppressionsPath, "{ \"rules\": [] }");

            var configJson = JsonSerializer.Serialize(new
            {
                suppressionsPath,
            });
            File.WriteAllText(configPath, configJson);

            var cli = new CliOptions
            {
                Command = "scan",
                ConnectionString = "Server=.;Database=Db;Trusted_Connection=True;",
                ConfigPath = configPath,
                SuppressionsPath = null,
                OutputDirectory = null,
                MarkdownPath = null,
                JsonPath = null,
                FixesDirectory = null,
                Profile = null,
                OutputFormat = null,
                NonInteractive = false,
                Preset = null,
                ActiveCheckIds = null,
                AuditOptionOverrides = new AuditOptionsOverrides(),
            };

            var resolved = ProjectConfigurationResolver.Resolve(cli, environmentConnectionString: null);

            Assert.Equal(Path.GetFullPath(suppressionsPath), resolved.SuppressionsPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
