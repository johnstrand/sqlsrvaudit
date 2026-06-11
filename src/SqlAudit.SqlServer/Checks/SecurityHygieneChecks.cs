using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;

namespace SqlAudit.SqlServer.Checks;

internal sealed class SecurityHygieneCheck : IHealthCheck
{
    public string Id => "SEC-001";

    public string Title => "Security hygiene issues detected";

    public string Category => "Security";

    public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var issues = context.Snapshot.SecurityHygieneIssues;
        if (issues.Count == 0)
        {
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>([]);
        }

        var findings = new List<AuditFinding>();

        foreach (var issue in issues)
        {
            findings.Add(new AuditFinding
            {
                Id = $"SEC-001-{issue.IssueType.ToUpperInvariant()}-{issue.Principal.Replace(' ', '-').ToUpperInvariant()}",
                Title = $"Security hygiene issue: {issue.IssueType} for {issue.Principal}",
                Category = Category,
                Severity = issue.Severity,
                DatabaseObject = issue.Principal,
                Description = issue.Details,
                Impact = "Security misconfigurations can lead to unauthorized access, privilege escalation, or unintended permissions.",
                Recommendation = "Review the principal's permissions and role memberships. Drop orphan users or remove excessive privileges.",
                ServiceWindow = ServiceWindowAdvisor.No("Security changes generally do not require downtime, though applications using affected accounts may experience disruption if privileges are incorrectly revoked."),
                FixScript = $"-- Review and manually resolve security hygiene issue: {issue.IssueType} for {issue.Principal}\n-- {issue.Details}",
                Evidence =
                [
                    new FindingEvidence("IssueType", issue.IssueType),
                    new FindingEvidence("Principal", issue.Principal),
                ],
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}
