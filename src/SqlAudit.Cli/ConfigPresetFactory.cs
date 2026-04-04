using SqlAudit.Core.Models;

namespace SqlAudit.Cli;

internal static class ConfigPresetFactory
{
    public static ProjectConfigFile Create(ConfigPreset preset)
    {
        return preset switch
        {
            ConfigPreset.Quick => new ProjectConfigFile
            {
                Profile = AuditProfile.Quick,
                OutputFormat = OutputFormat.Both,
                OutputDirectory = "audit-output/quick",
                AuditOptions = new AuditOptionsOverrides
                {
                    LargeTableRowThreshold = 250000,
                    UnusedIndexMinUpdates = 20000,
                    UnusedIndexMaxReads = 25,
                    IdentityUsageWarningPercent = 85,
                    IdentityUsageCriticalPercent = 97
                }
            },
            ConfigPreset.DeepStrict => new ProjectConfigFile
            {
                Profile = AuditProfile.Deep,
                OutputFormat = OutputFormat.Both,
                OutputDirectory = "audit-output/deep-strict",
                AuditOptions = new AuditOptionsOverrides
                {
                    LargeTableRowThreshold = 50000,
                    UnusedIndexMinUpdates = 5000,
                    UnusedIndexMaxReads = 20,
                    FragmentationMinPageCount = 500,
                    FragmentationReorganizeThresholdPercent = 8,
                    FragmentationRebuildThresholdPercent = 20,
                    LowPageDensityThresholdPercent = 75,
                    StaleStatsModificationPercent = 10,
                    StaleStatsMinModifications = 250,
                    IdentityUsageWarningPercent = 75,
                    IdentityUsageCriticalPercent = 90
                }
            },
            _ => new ProjectConfigFile
            {
                Profile = AuditProfile.Deep,
                OutputFormat = OutputFormat.Both,
                OutputDirectory = "audit-output/deep",
                AuditOptions = new AuditOptionsOverrides
                {
                    LargeTableRowThreshold = 100000,
                    UnusedIndexMinUpdates = 10000,
                    UnusedIndexMaxReads = 50,
                    FragmentationMinPageCount = 1000,
                    FragmentationReorganizeThresholdPercent = 10,
                    FragmentationRebuildThresholdPercent = 30,
                    LowPageDensityThresholdPercent = 70,
                    StaleStatsModificationPercent = 20,
                    StaleStatsMinModifications = 500,
                    IdentityUsageWarningPercent = 80,
                    IdentityUsageCriticalPercent = 95
                }
            }
        };
    }
}
