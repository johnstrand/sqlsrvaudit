using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;
using System.Globalization;

namespace SqlAudit.SqlServer.Checks;

internal sealed class DominantWaitCategoryCheck : IHealthCheck
{
    private const decimal DominanceThresholdPercent = 50m;
    private const decimal MinTotalWaitSeconds = 10m;

    public string Id => "WAIT-001";

    public string Title => "Dominant wait category detected";

    public string Category => "Performance";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var waits = context.Snapshot.TopWaitStats;
        if (waits.Count == 0)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var totalWait = waits.Sum(w => w.WaitTimeSeconds);
        if (totalWait < MinTotalWaitSeconds)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var findings = new List<AuditFinding>();

        var byCategory = waits
            .GroupBy(w => w.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Category: g.Key, WaitSeconds: g.Sum(w => w.WaitTimeSeconds), TopTypes: g.OrderByDescending(w => w.WaitTimeSeconds).Take(3).ToArray()))
            .OrderByDescending(c => c.WaitSeconds);

        foreach (var (category, waitSeconds, topTypes) in byCategory)
        {
            var pct = totalWait == 0 ? 0 : waitSeconds / totalWait * 100;
            if (pct < DominanceThresholdPercent)
            {
                continue;
            }

            var (description, recommendation) = GetCategoryGuidance(category);
            var topTypeNames = string.Join(", ", topTypes.Select(t => t.WaitType));

            findings.Add(new AuditFinding
            {
                Id = $"WAIT-001-{category.ToUpperInvariant().Replace(' ', '-')}",
                Title = $"Wait category '{category}' dominates server wait time",
                Category = Category,
                Severity = AuditSeverity.Medium,
                DatabaseObject = context.Snapshot.DatabaseName,
                Description = description,
                Impact = $"'{category}' accounts for {pct.ToString("0.#", CultureInfo.InvariantCulture)}% of total wait time ({waitSeconds.ToString("0.##", CultureInfo.InvariantCulture)}s of {totalWait.ToString("0.##", CultureInfo.InvariantCulture)}s). Dominant waits in one category typically indicate a systemic bottleneck.",
                Recommendation = recommendation,
                ServiceWindow = ServiceWindowAdvisor.No("Observational finding — no schema change required."),
                Evidence =
                [
                    new FindingEvidence("DominantCategory", category),
                    new FindingEvidence("CategoryWaitSeconds", waitSeconds.ToString("0.##", CultureInfo.InvariantCulture)),
                    new FindingEvidence("TotalWaitSeconds", totalWait.ToString("0.##", CultureInfo.InvariantCulture)),
                    new FindingEvidence("DominancePercent", pct.ToString("0.#", CultureInfo.InvariantCulture) + "%"),
                    new FindingEvidence("TopWaitTypes", topTypeNames),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }

    private static (string Description, string Recommendation) GetCategoryGuidance(string category)
    {
        return category switch
        {
            "I/O" => (
                "The server is spending significant time waiting for disk I/O. This often indicates storage throughput saturation, missing indexes causing large scans, or buffer pool pressure.",
                "Review disk latency counters, identify large scan queries via the Top Resource-Intensive Queries section, ensure indexes exist for high-read workloads, and consider increasing buffer pool memory or upgrading storage."),
            "Locking" => (
                "Locking waits dominate, indicating frequent blocking between concurrent sessions.",
                "Investigate active blocking sessions (see Blocking and Deadlocks section), review transaction isolation levels, ensure indexes are in place to shorten lock hold times, and consider READ_COMMITTED_SNAPSHOT isolation."),
            "Memory" => (
                "The server is waiting on memory grants or buffer pool resources, indicating memory pressure.",
                "Check for large sort and hash operations in query plans, review memory grant settings (MAX_GRANT_PERCENT), and consider adding RAM or limiting max server memory to reserve OS headroom."),
            "Parallelism" => (
                "CX (parallelism coordination) waits are high, which can indicate excessive parallel query plans or MAXDOP misconfiguration.",
                "Review MAXDOP and Cost Threshold for Parallelism settings, identify queries generating wide parallel plans, and consider query-level MAXDOP hints for OLTP workloads."),
            "CPU/Scheduler" => (
                "CPU scheduling waits are elevated, suggesting the server is CPU-bound or threads are competing for scheduler time.",
                "Profile high-CPU queries using the Top Resource-Intensive Queries section, look for missing index signals, and review MAXDOP. Consider upgrading CPU capacity if the workload is genuinely CPU-limited."),
            "Network" => (
                "Network I/O waits are elevated, which often indicates that clients are slow to consume result sets or large result sets are being returned.",
                "Investigate result-set sizes from application queries, check for row-by-row cursor-based processing, and review network bandwidth between application and database server."),
            _ => (
                $"The '{category}' wait category accounts for a large share of total wait time.",
                "Investigate the specific wait types in this category and correlate with query activity in the Top Resource-Intensive Queries section."),
        };
    }
}

internal sealed class CpuPressureCheck : IHealthCheck
{
    private const decimal SignalWaitRatioThreshold = 0.25m;
    private const decimal MinTotalWaitSeconds = 10m;

    public string Id => "WAIT-002";

    public string Title => "High CPU scheduler pressure (signal wait ratio)";

    public string Category => "Performance";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var waits = context.Snapshot.TopWaitStats;
        if (waits.Count == 0)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var totalWait = waits.Sum(w => w.WaitTimeSeconds);
        var totalSignal = waits.Sum(w => w.SignalWaitSeconds);

        if (totalWait < MinTotalWaitSeconds)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var ratio = totalSignal / totalWait;
        if (ratio < SignalWaitRatioThreshold)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var pct = ratio * 100;
        var severity = ratio >= 0.40m ? AuditSeverity.High : AuditSeverity.Medium;

        var finding = new AuditFinding
        {
            Id = "WAIT-002-CPU-PRESSURE",
            Title = "High signal-wait ratio indicates CPU pressure",
            Category = Category,
            Severity = severity,
            DatabaseObject = context.Snapshot.DatabaseName,
            Description = "Signal wait time is the time threads spend waiting to be scheduled onto a CPU after becoming runnable. A high signal-wait ratio (signal / total wait) suggests that CPU is a bottleneck — threads are ready to run but cannot get scheduled promptly.",
            Impact = $"Signal waits are {pct.ToString("0.#", CultureInfo.InvariantCulture)}% of total wait time ({totalSignal.ToString("0.##", CultureInfo.InvariantCulture)}s signal of {totalWait.ToString("0.##", CultureInfo.InvariantCulture)}s total). This typically means workload demand exceeds CPU capacity.",
            Recommendation = "Identify the highest-CPU queries using the Top Resource-Intensive Queries section and look for missing indexes, implicit conversions, or parallelism issues. Review MAXDOP and Cost Threshold for Parallelism. Consider upgrading CPU if the workload is genuinely compute-bound.",
            ServiceWindow = ServiceWindowAdvisor.No("Observational finding — no schema change required."),
            Evidence =
            [
                new FindingEvidence("TotalWaitSeconds", totalWait.ToString("0.##", CultureInfo.InvariantCulture)),
                new FindingEvidence("SignalWaitSeconds", totalSignal.ToString("0.##", CultureInfo.InvariantCulture)),
                new FindingEvidence("SignalWaitRatio", pct.ToString("0.#", CultureInfo.InvariantCulture) + "%"),
            ],
        };

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>([finding]);
    }
}
