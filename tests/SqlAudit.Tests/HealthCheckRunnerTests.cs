using SqlAudit.Core.Abstractions;
using SqlAudit.Core.Execution;
using SqlAudit.Core.Models;

namespace SqlAudit.Tests;

public sealed class HealthCheckRunnerTests
{
    [Fact]
    public async Task RunAsync_RecordsSuccessExecutionsAndFindings()
    {
        var checkA = new StubCheck(
            id: "CHK-001",
            execute: () =>
            [
                CreateFinding("CHK-001-1", "[dbo].[Books]"),
            ]);
        var checkB = new StubCheck(id: "CHK-002", execute: () => []);

        var runner = new HealthCheckRunner([checkA, checkB]);
        var result = await runner.RunAsync(CreateContext(), CancellationToken.None);

        Assert.Single(result.Findings);
        Assert.Equal(2, result.CheckExecutions.Count);

        var executionA = Assert.Single(result.CheckExecutions, e => string.Equals(e.CheckId, "CHK-001", StringComparison.Ordinal));
        Assert.Equal(CheckExecutionStatus.Success, executionA.Status);
        Assert.Equal(1, executionA.FindingCount);

        var executionB = Assert.Single(result.CheckExecutions, e => string.Equals(e.CheckId, "CHK-002", StringComparison.Ordinal));
        Assert.Equal(CheckExecutionStatus.Success, executionB.Status);
        Assert.Equal(0, executionB.FindingCount);
    }

    [Fact]
    public async Task RunAsync_TransformsCheckFailureIntoExecutionFinding()
    {
        var failing = new StubCheck(
            id: "CHK-BOOM",
            execute: () => throw new InvalidOperationException("boom"));

        var runner = new HealthCheckRunner([failing]);
        var result = await runner.RunAsync(CreateContext(), CancellationToken.None);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("CHECK-FAIL-CHK-BOOM", finding.Id);
        Assert.Equal("Execution", finding.Category);
        Assert.Equal(AuditSeverity.High, finding.Severity);
        Assert.Contains(finding.Evidence, e => string.Equals(e.Name, "Error", StringComparison.Ordinal) && string.Equals(e.Value, "boom", StringComparison.Ordinal));

        var execution = Assert.Single(result.CheckExecutions);
        Assert.Equal(CheckExecutionStatus.Failed, execution.Status);
        Assert.Equal(0, execution.FindingCount);
        Assert.Equal("boom", execution.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_ThrowsWhenCancellationAlreadyRequested()
    {
        var neverCalled = new StubCheck(id: "CHK-001", execute: () => []);
        var runner = new HealthCheckRunner([neverCalled]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(CreateContext(), cts.Token));
        Assert.False(neverCalled.Executed);
    }

    private static HealthCheckContext CreateContext()
    {
        return new HealthCheckContext
        {
            Snapshot = new DatabaseSnapshot
            {
                ServerName = "server01",
                DatabaseName = "DbA",
                Edition = "Developer",
                ProductVersion = "16.0",
                CompatibilityLevel = 160,
                IsAzureSql = false,
                AutoCreateStatisticsOn = true,
                AutoUpdateStatisticsOn = true,
                Tables = [],
                Indexes = [],
                IndexUsage = [],
                IndexPhysicalStats = [],
                ForeignKeys = [],
                Statistics = [],
                IdentityColumns = [],
            },
            Options = AuditOptions.Default,
        };
    }

    private static AuditFinding CreateFinding(string id, string databaseObject)
    {
        return new AuditFinding
        {
            Id = id,
            Title = "Finding",
            Category = "Tests",
            Severity = AuditSeverity.Low,
            DatabaseObject = databaseObject,
            Description = "desc",
            Impact = "impact",
            Recommendation = "reco",
            ServiceWindow = ServiceWindowAdvisor.No("none"),
        };
    }

    private sealed class StubCheck(string id, Func<IReadOnlyCollection<AuditFinding>> execute) : IHealthCheck
    {
        private readonly Func<IReadOnlyCollection<AuditFinding>> execute = execute;

        public bool Executed { get; private set; }

        public string Id => id;

        public string Title => id;

        public string Category => "Tests";

        public Task<IReadOnlyCollection<AuditFinding>> ExecuteAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            Executed = true;
            return Task.FromResult(execute());
        }
    }
}
