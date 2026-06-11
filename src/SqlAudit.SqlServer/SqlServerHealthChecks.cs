using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Models;
using SqlAudit.SqlServer.Checks;

namespace SqlAudit.SqlServer;

public static class SqlServerHealthChecks
{
    private static readonly IReadOnlyList<CheckRegistration> Registrations =
    [
        new("PK-001", QuickEnabled: true, DeepEnabled: true, () => new MissingPrimaryKeyCheck()),
        new("HEAP-001", QuickEnabled: true, DeepEnabled: true, () => new LargeHeapTableCheck()),
        new("FK-001", QuickEnabled: true, DeepEnabled: true, () => new ForeignKeyDisabledOrUntrustedCheck()),
        new("FK-002", QuickEnabled: true, DeepEnabled: true, () => new ForeignKeyWithoutIndexCheck()),
        new("FK-003", QuickEnabled: true, DeepEnabled: true, () => new ForeignKeyTypeMismatchCheck()),
        new("IDX-001", QuickEnabled: true, DeepEnabled: true, () => new DuplicateIndexCheck()),
        new("IDX-002", QuickEnabled: false, DeepEnabled: true, () => new OverlappingIndexCheck()),
        new("IDX-003", QuickEnabled: true, DeepEnabled: true, () => new DisabledIndexCheck()),
        new("IDX-004", QuickEnabled: true, DeepEnabled: true, () => new UnusedIndexCheck()),
        new("IDX-005", QuickEnabled: false, DeepEnabled: true, () => new FragmentationCheck()),
        new("IDX-006", QuickEnabled: false, DeepEnabled: true, () => new LowPageDensityCheck()),
        new("IDX-007", QuickEnabled: false, DeepEnabled: true, () => new FillFactorAnomalyCheck()),
        new("STAT-001", QuickEnabled: false, DeepEnabled: true, () => new StaleStatisticsCheck()),
        new("STAT-002", QuickEnabled: true, DeepEnabled: true, () => new StatisticsConfigurationCheck()),
        new("CFG-001", QuickEnabled: true, DeepEnabled: true, () => new CompatibilityLevelCheck()),
        new("CAP-001", QuickEnabled: true, DeepEnabled: true, () => new IdentityExhaustionCheck()),
        new("IDX-008", QuickEnabled: false, DeepEnabled: true, () => new OverWideIndexKeyCheck()),
        new("WAIT-001", QuickEnabled: true, DeepEnabled: true, () => new DominantWaitCategoryCheck()),
        new("WAIT-002", QuickEnabled: true, DeepEnabled: true, () => new CpuPressureCheck()),
        new("COL-001", QuickEnabled: false, DeepEnabled: true, () => new NullableColumnWithNoNullsCheck()),
        new("COL-002", QuickEnabled: true, DeepEnabled: true, () => new OversizedStringColumnCheck()),
        new("CFG-002", QuickEnabled: true, DeepEnabled: true, () => new MaxDopConfigurationCheck()),
        new("CFG-003", QuickEnabled: true, DeepEnabled: true, () => new CostThresholdForParallelismCheck()),
        new("CFG-004", QuickEnabled: true, DeepEnabled: true, () => new OptimizeForAdHocWorkloadsCheck()),
        new("CFG-005", QuickEnabled: true, DeepEnabled: true, () => new MaxServerMemoryCheck()),
        new("TMPDB-001", QuickEnabled: true, DeepEnabled: true, () => new TempDbFileCountCheck()),
        new("TMPDB-002", QuickEnabled: true, DeepEnabled: true, () => new TempDbFileSizeEqualityCheck()),
        new("MAINT-001", QuickEnabled: true, DeepEnabled: true, () => new IntegrityCheckRecencyCheck()),
        new("IDX-009", QuickEnabled: true, DeepEnabled: true, () => new ScanHeavyIndexCheck()),
        new("IDX-010", QuickEnabled: true, DeepEnabled: true, () => new WriteAmplificationIndexCheck()),
        new("SESS-001", QuickEnabled: true, DeepEnabled: true, () => new SleepingOpenTransactionCheck()),
        new("MEM-001", QuickEnabled: true, DeepEnabled: true, () => new PageLifeExpectancyCheck()),
        new("IO-001", QuickEnabled: true, DeepEnabled: true, () => new DataFileReadLatencyCheck()),
        new("IO-002", QuickEnabled: true, DeepEnabled: true, () => new LogFileWriteLatencyCheck()),
        new("CACHE-001", QuickEnabled: true, DeepEnabled: true, () => new SingleUsePlanRatioCheck()),
        new("COMP-001", QuickEnabled: false, DeepEnabled: true, () => new UncompressedLargeTableCheck()),
        new("LOG-001", QuickEnabled: true, DeepEnabled: true, () => new HighVlfCountCheck()),
        new("LOG-002", QuickEnabled: true, DeepEnabled: true, () => new LogReuseWaitCheck()),
        new("BAK-001", QuickEnabled: true, DeepEnabled: true, () => new FullBackupRecencyCheck()),
        new("BAK-002", QuickEnabled: true, DeepEnabled: true, () => new LogBackupForFullRecoveryCheck()),
        new("BAK-003", QuickEnabled: true, DeepEnabled: true, () => new DifferentialBackupGapCheck()),
        new("DB-001", QuickEnabled: true, DeepEnabled: true, () => new AutoShrinkCheck()),
        new("DB-002", QuickEnabled: true, DeepEnabled: true, () => new AutoCloseCheck()),
        new("DB-003", QuickEnabled: true, DeepEnabled: true, () => new PageVerifyCheck()),
        new("DB-004", QuickEnabled: true, DeepEnabled: true, () => new RcsiAdvisoryCheck()),
        new("DB-005", QuickEnabled: true, DeepEnabled: true, () => new QueryStoreDisabledCheck()),
        new("DB-006", QuickEnabled: true, DeepEnabled: true, () => new QueryStoreReadOnlyCheck()),
        new("STOR-001", QuickEnabled: true, DeepEnabled: true, () => new LowDiskSpaceCheck()),
        new("STOR-002", QuickEnabled: true, DeepEnabled: true, () => new DataAndLogOnSameVolumeCheck()),
        new("IDX-011", QuickEnabled: false, DeepEnabled: true, () => new ColumnstoreOpportunityCheck()),
        new("MAINT-002", QuickEnabled: true, DeepEnabled: true, () => new FailedAgentJobsCheck()),
        new("CFG-006", QuickEnabled: true, DeepEnabled: true, () => new HarmfulTraceFlagCheck()),
        new("SEC-001", QuickEnabled: true, DeepEnabled: true, () => new SecurityHygieneCheck()),
    ];

    public static IReadOnlyCollection<IHealthCheck> Create(
        AuditProfile profile,
        IReadOnlyCollection<string>? activeCheckIds = null)
    {
        var eligible = Registrations
            .Where(r => IsProfileEnabled(r, profile))
            .ToArray();

        if (activeCheckIds is null || activeCheckIds.Count == 0)
        {
            return [.. eligible.Select(r => r.Factory())];
        }

        var active = activeCheckIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [.. eligible
            .Where(r => active.Contains(r.Id))
            .Select(r => r.Factory()),];
    }

    public static IReadOnlyCollection<IHealthCheck> CreateDefault() => CreateDeep();

    public static IReadOnlyCollection<IHealthCheck> CreateQuick() => Create(AuditProfile.Quick);

    public static IReadOnlyCollection<IHealthCheck> CreateDeep() => Create(AuditProfile.Deep);

    public static IReadOnlyList<CheckDescriptor> GetDescriptors(AuditProfile profile)
    {
        return [.. Registrations
            .Where(r => IsProfileEnabled(r, profile))
            .Select(r =>
            {
                var check = r.Factory();
                return new CheckDescriptor(check.Id, check.Title, check.Category, r.QuickEnabled, r.DeepEnabled);
            }),];
    }

    private static bool IsProfileEnabled(CheckRegistration registration, AuditProfile profile)
    {
        return profile switch
        {
            AuditProfile.Quick => registration.QuickEnabled,
            _ => registration.DeepEnabled,
        };
    }

    private sealed record CheckRegistration(string Id, bool QuickEnabled, bool DeepEnabled, Func<IHealthCheck> Factory);
}

public sealed record CheckDescriptor(
    string Id,
    string Title,
    string Category,
    bool QuickEnabled,
    bool DeepEnabled);
