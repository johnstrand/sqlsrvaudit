using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;
using System.Globalization;

namespace SqlAudit.SqlServer.Checks;

internal sealed class FailedAgentJobsCheck : IHealthCheck
{
    public string Id => "MAINT-002";

    public string Title => "SQL Agent jobs have recent failures";

    public string Category => "Maintenance";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var jobs = context.Snapshot.FailedAgentJobs;
        if (jobs.Count == 0)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var now = context.Snapshot.CapturedAtUtc;
        var findings = new List<AuditFinding>();

        foreach (var job in jobs)
        {
            var ageHours = (now - job.LastRunUtc).TotalHours;
            var severity = ageHours <= 24 ? AuditSeverity.High : AuditSeverity.Medium;

            findings.Add(new AuditFinding
            {
                Id = $"MAINT-002-{job.JobName.Replace(' ', '-').ToUpperInvariant()}",
                Title = $"SQL Agent job '{job.JobName}' has failed recently",
                Category = Category,
                Severity = severity,
                DatabaseObject = job.JobName,
                Description = $"SQL Agent job '{job.JobName}' (step: '{job.StepName}') failed {ageHours.ToString("F0", CultureInfo.InvariantCulture)} hours ago. Error: {job.ErrorMessage}",
                Impact = "Failed maintenance jobs (index rebuild, backup, statistics update, integrity checks) silently degrade database health over time without alerting the operator.",
                Recommendation = "Investigate and resolve the job failure. Review the job history in SSMS → SQL Server Agent → Jobs for detailed error information.",
                ServiceWindow = ServiceWindowAdvisor.No("Observational finding — investigate and fix the job failure."),
                Evidence =
                [
                    new FindingEvidence("JobName", job.JobName),
                    new FindingEvidence("StepName", job.StepName),
                    new FindingEvidence("LastRunUtc", job.LastRunUtc.ToString("u")),
                    new FindingEvidence("AgeHours", ageHours.ToString("F0", CultureInfo.InvariantCulture)),
                    new FindingEvidence("ErrorMessage", job.ErrorMessage.Length > 200 ? job.ErrorMessage[..200] + "…" : job.ErrorMessage),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}
