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
                outputDataModel = true,
                excludeSchemas = new[] { "archive", "ARCHIVE", " " },
                excludeTables = new[] { "Book_Backup", "dbo.Book_Backup", "Book_Backup", " " },
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
                OutputDataModel = false,
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
            Assert.True(resolved.OutputDataModel);
            Assert.Equal(999, resolved.AuditOptions.StaleStatsMinModifications);
            Assert.Equal(91, resolved.AuditOptions.IdentityUsageWarningPercent);
            Assert.Contains("out-from-config", resolved.OutputDirectory, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine("out-from-config", "data-model.json"), resolved.DataModelPath, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(resolved.ExcludeSchemas);
            Assert.Single(resolved.ExcludeSchemas!);
            Assert.Equal("archive", resolved.ExcludeSchemas![0], ignoreCase: true);
            Assert.NotNull(resolved.ExcludeTables);
            Assert.Equal(2, resolved.ExcludeTables!.Count);
            Assert.Contains("Book_Backup", resolved.ExcludeTables, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("dbo.Book_Backup", resolved.ExcludeTables, StringComparer.OrdinalIgnoreCase);
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
    public void Resolve_UsesFailOnFromConfigFileWhenCliOptionNotSpecified()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SqlAuditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var configPath = Path.Combine(tempDir, "sqlaudit.project.json");
            var configJson = JsonSerializer.Serialize(new
            {
                connectionString = "Server=.;Database=Db;Trusted_Connection=True;",
                failOn = "High",
            });

            File.WriteAllText(configPath, configJson);

            var cli = new CliOptions
            {
                Command = "scan",
                ConfigPath = configPath,
                AuditOptionOverrides = new AuditOptionsOverrides(),
            };

            var resolved = ProjectConfigurationResolver.Resolve(cli, environmentConnectionString: null);

            Assert.Equal(AuditSeverity.High, resolved.FailOnSeverity);
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
    public void Resolve_UsesOutputDataModelFromCliFlag()
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
            OutputDataModel = true,
            Profile = null,
            OutputFormat = null,
            NonInteractive = false,
            Preset = null,
            ActiveCheckIds = null,
            AuditOptionOverrides = new AuditOptionsOverrides(),
        };

        var resolved = ProjectConfigurationResolver.Resolve(cli, environmentConnectionString: null);

        Assert.True(resolved.OutputDataModel);
        Assert.EndsWith(Path.Combine("audit-output", "data-model.json"), resolved.DataModelPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_LoadsEmbeddedPresetWhenConfigPathUsesPresetAlias()
    {
        var cli = new CliOptions
        {
            Command = "scan",
            ConnectionString = "Server=.;Database=Db;Trusted_Connection=True;",
            ConfigPath = "preset:deep-strict",
            SuppressionsPath = null,
            OutputDirectory = null,
            MarkdownPath = null,
            JsonPath = null,
            FixesDirectory = null,
            Profile = null,
            OutputFormat = null,
            OutputDataModel = false,
            NonInteractive = false,
            Preset = null,
            ActiveCheckIds = null,
            AuditOptionOverrides = new AuditOptionsOverrides(),
        };

        var resolved = ProjectConfigurationResolver.Resolve(cli, environmentConnectionString: null);

        Assert.Equal(AuditProfile.Deep, resolved.Profile);
        Assert.Equal(OutputFormat.Both, resolved.Format);
        Assert.Equal(500, resolved.AuditOptions.FragmentationMinPageCount);
        Assert.Equal(20, resolved.AuditOptions.FragmentationRebuildThresholdPercent);
        Assert.Contains(Path.Combine("audit-output", "deep-strict"), resolved.OutputDirectory, StringComparison.OrdinalIgnoreCase);
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
            OutputDataModel = false,
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
                OutputDataModel = false,
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

    [Fact]
    public void Resolve_UsesCommandTimeoutFromConfigFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SqlAuditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var configPath = Path.Combine(tempDir, "sqlaudit.project.json");
            var configJson = JsonSerializer.Serialize(new
            {
                connectionString = "Server=.;Database=Db;Trusted_Connection=True;",
                commandTimeout = 75,
            });
            File.WriteAllText(configPath, configJson);

            var cli = new CliOptions
            {
                Command = "scan",
                ConfigPath = configPath,
                AuditOptionOverrides = new AuditOptionsOverrides(),
            };

            var resolved = ProjectConfigurationResolver.Resolve(cli, environmentConnectionString: null);

            Assert.Equal(75, resolved.CommandTimeout);
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
    public void Resolve_CliTimeoutOverridesConfigFileTimeout()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SqlAuditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var configPath = Path.Combine(tempDir, "sqlaudit.project.json");
            var configJson = JsonSerializer.Serialize(new
            {
                connectionString = "Server=.;Database=Db;Trusted_Connection=True;",
                commandTimeout = 75,
            });
            File.WriteAllText(configPath, configJson);

            var cli = new CliOptions
            {
                Command = "scan",
                ConfigPath = configPath,
                CommandTimeout = 90,
                AuditOptionOverrides = new AuditOptionsOverrides(),
            };

            var resolved = ProjectConfigurationResolver.Resolve(cli, environmentConnectionString: null);

            Assert.Equal(90, resolved.CommandTimeout);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Resolve_ThrowsWhenCommandTimeoutIsZeroOrNegative(int timeout)
    {
        var cli = new CliOptions
        {
            Command = "scan",
            ConnectionString = "Server=.;Database=Db;Trusted_Connection=True;",
            CommandTimeout = timeout,
            AuditOptionOverrides = new AuditOptionsOverrides(),
        };

        var exception = Assert.Throws<InvalidOperationException>(() => ProjectConfigurationResolver.Resolve(cli, environmentConnectionString: null));
        Assert.Contains("Command timeout must be greater than zero", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_AllowsMissingConnectionStringWhenFromSnapshotPathIsProvided()
    {
        var cli = new CliOptions
        {
            Command = "scan",
            FromSnapshotPath = "audit-output/data-model.json",
            AuditOptionOverrides = new AuditOptionsOverrides(),
        };

        var resolved = ProjectConfigurationResolver.Resolve(cli, environmentConnectionString: null);

        Assert.Equal(string.Empty, resolved.ConnectionString);
        Assert.EndsWith(Path.Combine("audit-output", "data-model.json"), resolved.FromSnapshotPath!, StringComparison.OrdinalIgnoreCase);
    }
}
