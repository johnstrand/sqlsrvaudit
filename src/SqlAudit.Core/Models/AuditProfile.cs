namespace SqlAudit.Core.Models;

public enum AuditProfile
{
    Quick,
    Deep,
}

public static class AuditProfileDefaults
{
    public static AuditOptions For(AuditProfile profile) => profile switch
    {
        AuditProfile.Quick => new AuditOptions
        {
            LargeTableRowThreshold = 250_000,
            UnusedIndexMinUpdates = 20_000,
            UnusedIndexMaxReads = 25,
            FragmentationMinPageCount = 2_000,
            FragmentationReorganizeThresholdPercent = 10,
            FragmentationRebuildThresholdPercent = 30,
            LowPageDensityThresholdPercent = 70,
            StaleStatsModificationPercent = 20,
            StaleStatsMinModifications = 1_000,
            IdentityUsageWarningPercent = 85,
            IdentityUsageCriticalPercent = 97,
        },
        _ => new AuditOptions(),
    };
}
