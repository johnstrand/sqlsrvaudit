using System.Text.Json;
using SqlAudit.Cli;
using SqlAudit.Core.Models;

namespace SqlAudit.Tests;

public sealed class InitConfigTests
{
    [Fact]
    public void ConfigPresetFactory_DeepStrictUsesExpectedThresholds()
    {
        var preset = ConfigPresetFactory.Create(ConfigPreset.DeepStrict);

        Assert.Equal(AuditProfile.Deep, preset.Profile);
        Assert.NotNull(preset.AuditOptions);
        Assert.Equal(500, preset.AuditOptions!.FragmentationMinPageCount);
        Assert.Equal(20, preset.AuditOptions.FragmentationRebuildThresholdPercent);
        Assert.Equal(90, preset.AuditOptions.IdentityUsageCriticalPercent);
    }

    [Fact]
    public void InitConfig_NonInteractive_WritesPresetConfig()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SqlAuditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var configPath = Path.Combine(tempDir, "sqlaudit.project.json");
            var options = new CliOptions
            {
                Command = "init-config",
                ConnectionString = null,
                ConfigPath = configPath,
                OutputDirectory = null,
                MarkdownPath = null,
                JsonPath = null,
                FixesDirectory = null,
                Profile = null,
                OutputFormat = null,
                NonInteractive = true,
                Preset = ConfigPreset.Quick,
                ActiveCheckIds = null,
                AuditOptionOverrides = new AuditOptionsOverrides()
            };

            var exit = InteractiveConfigWizard.Run(options);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(configPath));

            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("quick", doc.RootElement.GetProperty("profile").GetString());
            Assert.Equal("both", doc.RootElement.GetProperty("outputFormat").GetString());
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
