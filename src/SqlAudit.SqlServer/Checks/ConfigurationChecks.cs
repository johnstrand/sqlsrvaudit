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
                Evidence =
                [
                    new FindingEvidence("MinFileSizeMb", minSize.ToString("F0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("MaxFileSizeMb", maxSize.ToString("F0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("FileCount", cfg.DataFileSizesMb.Count.ToString(CultureInfo.InvariantCulture)),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}
