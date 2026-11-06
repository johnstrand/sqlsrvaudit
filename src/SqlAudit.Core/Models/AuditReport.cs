namespace SqlAudit.Core.Models;

/// <summary>
/// The master root document containing the complete output of an audit run, including environment metadata, findings, and telemetry.
/// </summary>
public sealed record AuditReport
{
    public string SchemaVersion { get; init; } = "1.5";

    public required string ServerName { get; init; }

    public required string DatabaseName { get; init; }

    public required string Edition { get; init; }

    public required string ProductVersion { get; init; }

    public required DateTimeOffset CapturedAtUtc { get; init; }

    private IReadOnlyList<AuditFinding> _findings = [];
    private IReadOnlyDictionary<AuditSeverity, int>? _severityCounts;
    private IReadOnlyDictionary<string, int>? _categoryCounts;

    public required IReadOnlyList<AuditFinding> Findings
    {
        get => _findings;
        init
        {
            _findings = value;
            _severityCounts = value
                .GroupBy(f => f.Severity)
                .ToDictionary(g => g.Key, g => g.Count());
            _categoryCounts = value
                .GroupBy(f => f.Category, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        }
    }

    public IReadOnlyList<CheckExecutionResult> CheckExecutions { get; init; } = [];

    public IReadOnlyList<string> ExcludedSchemas { get; init; } = [];

    public IReadOnlyList<string> ExcludedTables { get; init; } = [];

    public IReadOnlyList<ResourceIntensiveQueryInfo> TopResourceIntensiveQueries { get; init; } = [];

    public IReadOnlyList<WaitStatInfo> TopWaitStats { get; init; } = [];

    public IReadOnlyList<QueryStoreRegressionInfo> QueryStoreRegressions { get; init; } = [];

    public IReadOnlyList<BlockingSessionInfo> ActiveBlockingSessions { get; init; } = [];

    public DeadlockSummaryInfo? DeadlockSummary { get; init; }

    public IReadOnlyList<MissingIndexSignalInfo> MissingIndexSignals { get; init; } = [];

    public LogHealthInfo? LogHealth { get; init; }

    public TempDbPressureInfo? TempDbPressure { get; init; }

    public IReadOnlyList<FileGrowthHealthInfo> FileGrowthHealth { get; init; } = [];

    public BackupPostureInfo? BackupPosture { get; init; }

    public IReadOnlyList<SecurityHygieneIssueInfo> SecurityHygieneIssues { get; init; } = [];

    public IReadOnlyList<TableGrowthForecastInfo> TableGrowthForecasts { get; init; } = [];

    public IReadOnlyList<CollectionWarning> CollectionWarnings { get; init; } = [];

    public IReadOnlyList<ServerConfigInfo> ServerConfigurations { get; init; } = [];

    public DateTimeOffset? LastDbccCheckDbUtc { get; init; }

    public TempDbConfigInfo? TempDbConfig { get; init; }

    public IReadOnlyList<SleepingTransactionInfo> SleepingTransactions { get; init; } = [];

    public MemoryPressureInfo? MemoryPressure { get; init; }

    public IReadOnlyList<FileIoLatencyInfo> FileIoLatency { get; init; } = [];

    public PlanCacheInfo? PlanCache { get; init; }

    public IReadOnlyList<TableCompressionInfo> TableCompression { get; init; } = [];

    public DatabaseOptionsInfo? DatabaseOptions { get; init; }

    public IReadOnlyList<VolumeInfo> VolumeStats { get; init; } = [];

    public IReadOnlyList<FailedAgentJobInfo> FailedAgentJobs { get; init; } = [];

    public IReadOnlyList<GlobalTraceFlagInfo> GlobalTraceFlags { get; init; } = [];

    public SuppressionSummary SuppressionSummary { get; init; } = SuppressionSummary.None;

    public IReadOnlyDictionary<AuditSeverity, int> SeverityCounts => _severityCounts ??= Findings
        .GroupBy(f => f.Severity)
        .ToDictionary(g => g.Key, g => g.Count());

    public IReadOnlyDictionary<string, int> CategoryCounts => _categoryCounts ??= Findings
        .GroupBy(f => f.Category, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
}
