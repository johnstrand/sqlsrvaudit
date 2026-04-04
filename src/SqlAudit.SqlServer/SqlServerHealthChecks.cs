using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Models;
using SqlAudit.SqlServer.Checks;

namespace SqlAudit.SqlServer;

public static class SqlServerHealthChecks
{
    private static readonly IReadOnlyList<CheckRegistration> Registrations =
    [
        new("PK-001", true, true, () => new MissingPrimaryKeyCheck()),
        new("HEAP-001", true, true, () => new LargeHeapTableCheck()),
        new("FK-001", true, true, () => new ForeignKeyDisabledOrUntrustedCheck()),
        new("FK-002", true, true, () => new ForeignKeyWithoutIndexCheck()),
        new("FK-003", true, true, () => new ForeignKeyTypeMismatchCheck()),
        new("IDX-001", true, true, () => new DuplicateIndexCheck()),
        new("IDX-002", false, true, () => new OverlappingIndexCheck()),
        new("IDX-003", true, true, () => new DisabledIndexCheck()),
        new("IDX-004", true, true, () => new UnusedIndexCheck()),
        new("IDX-005", false, true, () => new FragmentationCheck()),
        new("IDX-006", false, true, () => new LowPageDensityCheck()),
        new("IDX-007", false, true, () => new FillFactorAnomalyCheck()),
        new("STAT-001", false, true, () => new StaleStatisticsCheck()),
        new("STAT-002", true, true, () => new StatisticsConfigurationCheck()),
        new("CAP-001", true, true, () => new IdentityExhaustionCheck()),
        new("IDX-008", false, true, () => new OverWideIndexKeyCheck())
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
            .Select(r => r.Factory())];
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
            })];
    }

    private static bool IsProfileEnabled(CheckRegistration registration, AuditProfile profile)
    {
        return profile switch
        {
            AuditProfile.Quick => registration.QuickEnabled,
            _ => registration.DeepEnabled
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
