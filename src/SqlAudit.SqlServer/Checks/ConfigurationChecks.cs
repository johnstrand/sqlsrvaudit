using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;
using System.Globalization;

namespace SqlAudit.SqlServer.Checks;

internal sealed class MaxDopConfigurationCheck : IHealthCheck
{
    public string Id => "CFG-002";

    public string Title => "MAXDOP configuration";

    public string Category => "Configuration";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();
        var maxdop = context.Snapshot.ServerConfigurations
            .FirstOrDefault(c => c.Name.Equals("max degree of parallelism", StringComparison.OrdinalIgnoreCase));
        if (maxdop is null)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
        }

        var value = (int)maxdop.ValueInUse;
        if (value == 0)
        {
            findings.Add(new AuditFinding
            {
                Id = "CFG-002-MAXDOP-ZERO",
                Title = "MAXDOP is 0 (unlimited parallelism)",
                Category = Category,
                Severity = AuditSeverity.Medium,
                DatabaseObject = "server",
                Description = "MAXDOP = 0 allows the engine to use all available CPUs for a single query, which can cause CPU starvation for other workloads.",
                Impact = "Runaway parallel queries can saturate CPUs and degrade OLTP response times.",
                Recommendation = "Set MAXDOP to min(logical CPUs per NUMA node, 8) for OLTP workloads.",
                ServiceWindow = ServiceWindowAdvisor.No("sp_configure change takes effect immediately with RECONFIGURE; no restart or service window needed."),
                FixScript = "EXEC sys.sp_configure N'max degree of parallelism', 8; RECONFIGURE;",
                Evidence =
                [
                    new FindingEvidence("CurrentValue", value.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("Recommended", "1–8 (depends on NUMA topology)"),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class CostThresholdForParallelismCheck : IHealthCheck
{
    public string Id => "CFG-003";

    public string Title => "Cost threshold for parallelism";

    public string Category => "Configuration";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();
        var cfg = context.Snapshot.ServerConfigurations
            .FirstOrDefault(c => c.Name.Equals("cost threshold for parallelism", StringComparison.OrdinalIgnoreCase));
        if (cfg is null)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
        }

        var value = (int)cfg.ValueInUse;
        if (value <= 5)
        {
            findings.Add(new AuditFinding
            {
                Id = "CFG-003-CTP-DEFAULT",
                Title = "Cost threshold for parallelism is at default (5)",
                Category = Category,
                Severity = AuditSeverity.Medium,
                DatabaseObject = "server",
                Description = $"The cost threshold for parallelism is {value}, which is the SQL Server default. On modern hardware this is too low and causes trivial queries to go parallel unnecessarily.",
                Impact = "Excessive parallelism for cheap queries wastes threads and can cause thread pool pressure on busy servers.",
                Recommendation = "Set the cost threshold to 50 or higher for OLTP workloads. Profile actual query costs before setting.",
                ServiceWindow = ServiceWindowAdvisor.No("sp_configure change takes effect immediately with RECONFIGURE; no restart or service window needed."),
                FixScript = "EXEC sys.sp_configure N'cost threshold for parallelism', 50; RECONFIGURE;",
                Evidence =
                [
                    new FindingEvidence("CurrentValue", value.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("Recommended", "50+"),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class OptimizeForAdHocWorkloadsCheck : IHealthCheck
{
    public string Id => "CFG-004";

    public string Title => "Optimize for ad hoc workloads";

    public string Category => "Configuration";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();
        var cfg = context.Snapshot.ServerConfigurations
            .FirstOrDefault(c => c.Name.Equals("optimize for ad hoc workloads", StringComparison.OrdinalIgnoreCase));
        if (cfg is null)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
        }

        if ((int)cfg.ValueInUse == 0)
        {
            findings.Add(new AuditFinding
            {
                Id = "CFG-004-ADHOC-OFF",
                Title = "Optimize for ad hoc workloads is disabled",
                Category = Category,
                Severity = AuditSeverity.Low,
                DatabaseObject = "server",
                Description = "When disabled, every ad hoc query stores a full execution plan in the plan cache on first execution, even if never reused. This wastes buffer pool memory.",
                Impact = "Plan cache bloat reduces available buffer pool memory, increasing physical I/O.",
                Recommendation = "Enable 'optimize for ad hoc workloads'. This stores only a stub on first execution and only caches the full plan on second execution.",
                ServiceWindow = ServiceWindowAdvisor.No("sp_configure change takes effect immediately with RECONFIGURE; no restart or service window needed."),
                FixScript = "EXEC sys.sp_configure N'optimize for ad hoc workloads', 1; RECONFIGURE;",
                Evidence =
                [
                    new FindingEvidence("CurrentValue", "0 (disabled)"),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class MaxServerMemoryCheck : IHealthCheck
{
    private const long DefaultMaxMemory = 2147483647L;

    public string Id => "CFG-005";

    public string Title => "Max server memory";

    public string Category => "Configuration";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();
        var cfg = context.Snapshot.ServerConfigurations
            .FirstOrDefault(c => c.Name.Equals("max server memory (MB)", StringComparison.OrdinalIgnoreCase));
        if (cfg is null)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
        }

        if ((long)cfg.ValueInUse >= DefaultMaxMemory)
        {
            findings.Add(new AuditFinding
            {
                Id = "CFG-005-MAXMEM-UNLIMITED",
                Title = "Max server memory is unlimited",
                Category = Category,
                Severity = AuditSeverity.High,
                DatabaseObject = "server",
                Description = "Max server memory is set to the default unlimited value. SQL Server can consume all available OS memory, starving the OS and other services.",
                Impact = "OS paging, OOM conditions, and instability on shared or multi-service hosts.",
                Recommendation = "Set max server memory to leave at least 10% or 4 GB for the OS (whichever is larger). Use the formula: TotalRAM - max(4096, TotalRAM * 0.1).",
                ServiceWindow = ServiceWindowAdvisor.No("sp_configure change takes effect immediately with RECONFIGURE; no restart or service window needed."),
                FixScript = "-- TODO: Replace <value_mb> with the calculated appropriate value.\nEXEC sys.sp_configure N'max server memory (MB)', <value_mb>; RECONFIGURE;",
                Evidence =
                [
                    new FindingEvidence("CurrentValue", "2147483647 (unlimited)"),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class HarmfulTraceFlagCheck : IHealthCheck
{
    private static readonly Dictionary<int, (string Description, AuditSeverity Severity)> HarmfulFlags =
        new Dictionary<int, (string, AuditSeverity)>
        {
            [1117] = ("TF 1117 causes all files in a filegroup to grow together when any single file hits autogrowth. This behavior is now the default in SQL Server 2016+ and having TF 1117 enabled on newer versions is unnecessary and may cause unexpected storage growth patterns.", AuditSeverity.Low),
            [1118] = ("TF 1118 forces uniform extent allocations for all objects, eliminating mixed extent contention. Like TF 1117, this is now the default behavior in SQL Server 2016+ (for tempdb) and is unnecessary on modern versions.", AuditSeverity.Low),
            [3625] = ("TF 3625 masks system error messages shown to non-sysadmin users with a generic '%%' message. While this can hide internal details from end users, it also makes debugging production errors harder and can hide security-relevant information from DBAs.", AuditSeverity.Medium),
            [8744] = ("TF 8744 disables pre-fetching for nested loop operators. This was a workaround for a specific bug and should not be active in production unless directed by Microsoft Support for a known issue.", AuditSeverity.Medium),
            [9481] = ("TF 9481 forces the legacy cardinality estimator (CE70) for all queries. This is a broad blunt instrument — it prevents the query optimizer from using improved CE models and should not be applied globally; use query-level hints or database-scoped configuration instead.", AuditSeverity.Medium),
            [4199] = ("TF 4199 enables query optimizer hotfixes. While usually beneficial, applying it globally via trace flag (rather than database-scoped QUERY_OPTIMIZER_HOTFIXES=ON) means it cannot be selectively disabled per database, which can cause unexpected plan changes across all databases.", AuditSeverity.Low),
        };

    public string Id => "CFG-006";

    public string Title => "Potentially harmful global trace flags are enabled";

    public string Category => "Configuration";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = context.Snapshot.GlobalTraceFlags
            .Where(f => f.IsGlobal && HarmfulFlags.ContainsKey(f.TraceFlag))
            .Select(flag =>
            {
                var (description, severity) = HarmfulFlags[flag.TraceFlag];
                return new AuditFinding
                {
                    Id = $"CFG-006-TF{flag.TraceFlag}",
                    Title = $"Potentially harmful global trace flag TF {flag.TraceFlag} is enabled",
                    Category = Category,
                    Severity = severity,
                    DatabaseObject = "server",
                    Description = description,
                    Impact = "Global trace flags affect all databases and connections on the instance. Incorrect or outdated trace flags can cause unexpected behavior, degraded performance, or hidden errors.",
                    Recommendation = $"Review whether trace flag {flag.TraceFlag} is still needed. If it was applied as a workaround, verify the underlying issue is resolved and disable it.",
                    ServiceWindow = ServiceWindowAdvisor.No("DBCC TRACEOFF takes effect immediately; no service window required."),
                    FixScript = $"DBCC TRACEOFF({flag.TraceFlag}, -1); -- Disables TF {flag.TraceFlag} globally",
                    Evidence =
                    [
                        new FindingEvidence("TraceFlag", flag.TraceFlag.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new FindingEvidence("IsGlobal", "Yes"),
                    ],
                };
            })
            .ToList();

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class TempDbFileCountCheck : IHealthCheck
{
    public string Id => "TMPDB-001";

    public string Title => "TempDB data file count";

    public string Category => "TempDB";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();
        var cfg = context.Snapshot.TempDbConfig;
        if (cfg is null)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
        }

        var recommendedFiles = cfg.LogicalCpuCount > 0
            ? Math.Min(cfg.LogicalCpuCount, 8)
            : 8;

        if (cfg.DataFileCount < recommendedFiles)
        {
            findings.Add(new AuditFinding
            {
                Id = "TMPDB-001-FILECOUNT",
                Title = "TempDB has fewer data files than recommended",
                Category = Category,
                Severity = AuditSeverity.Medium,
                DatabaseObject = "tempdb",
                Description = $"TempDB has {cfg.DataFileCount} data file(s) but the server has {cfg.LogicalCpuCount} logical CPUs. Microsoft recommends min(logical CPUs, 8) data files to reduce allocation page latch contention.",
                Impact = "SGAM/GAM/PFS latch contention under concurrent TempDB workloads reduces throughput.",
                Recommendation = $"Add {recommendedFiles - cfg.DataFileCount} more TempDB data file(s) of equal size to the existing files.",
                ServiceWindow = ServiceWindowAdvisor.No("Adding TempDB files does not require a service window."),
                FixScript = $"-- TODO: Adjust path and size to match your existing TempDB files.\n-- Add {recommendedFiles - cfg.DataFileCount} data file(s):\nALTER DATABASE tempdb ADD FILE (NAME = N'tempdev2', FILENAME = N'<path>\\tempdev2.ndf', SIZE = 8MB, FILEGROWTH = 64MB);",
                Evidence =
                [
                    new FindingEvidence("CurrentFileCount", cfg.DataFileCount.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("LogicalCpuCount", cfg.LogicalCpuCount.ToString(CultureInfo.InvariantCulture)),
                    new FindingEvidence("RecommendedFileCount", recommendedFiles.ToString(CultureInfo.InvariantCulture)),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

internal sealed class TempDbFileSizeEqualityCheck : IHealthCheck
{
    public string Id => "TMPDB-002";

    public string Title => "TempDB data file size equality";

    public string Category => "TempDB";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var findings = new List<AuditFinding>();
        var cfg = context.Snapshot.TempDbConfig;
        if (cfg is null || cfg.DataFileSizesMb.Count < 2)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
        }

        var maxSize = cfg.DataFileSizesMb.Max();
        var minSize = cfg.DataFileSizesMb.Min();

        if (maxSize > 0 && (maxSize - minSize) / maxSize > 0.10m)
        {
            findings.Add(new AuditFinding
            {
                Id = "TMPDB-002-FILESIZE",
                Title = "TempDB data files have unequal sizes",
                Category = Category,
                Severity = AuditSeverity.Low,
                DatabaseObject = "tempdb",
                Description = $"TempDB data files vary in size from {minSize:F0} MB to {maxSize:F0} MB (>{10}% difference). SQL Server uses proportional fill, so larger files receive more allocations, defeating the purpose of having multiple equal files.",
                Impact = "Uneven allocation reduces the contention benefit of having multiple TempDB files.",
                Recommendation = "Resize all TempDB data files to be equal in size and set equal autogrowth.",
                ServiceWindow = ServiceWindowAdvisor.No("Resizing TempDB files does not require a service window, but do this during low-activity periods."),
                FixScript = $"-- TODO: Replace <target_size_mb> with the desired equal size.\n-- Resize all files:\nALTER DATABASE tempdb MODIFY FILE (NAME = N'tempdev', SIZE = <target_size_mb>MB);",
                Evidence = [
                    new FindingEvidence("MinFileSizeMb", minSize.ToString("F0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("MaxFileSizeMb", maxSize.ToString("F0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("FileCount", cfg.DataFileSizesMb.Count.ToString(CultureInfo.InvariantCulture)),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}
