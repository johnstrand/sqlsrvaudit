namespace SqlAudit.Core.Models;

public sealed class AuditOptions
{
    public long LargeTableRowThreshold { get; init; } = 100_000;
    public long UnusedIndexMinUpdates { get; init; } = 10_000;
    public long UnusedIndexMaxReads { get; init; } = 50;
    public int FragmentationMinPageCount { get; init; } = 1_000;
    public double FragmentationReorganizeThresholdPercent { get; init; } = 10;
    public double FragmentationRebuildThresholdPercent { get; init; } = 30;
    public double LowPageDensityThresholdPercent { get; init; } = 70;
    public double StaleStatsModificationPercent { get; init; } = 20;
    public long StaleStatsMinModifications { get; init; } = 500;
    public double IdentityUsageWarningPercent { get; init; } = 80;
    public double IdentityUsageCriticalPercent { get; init; } = 95;

    public static AuditOptions Default { get; } = new();
}
