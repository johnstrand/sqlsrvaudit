namespace SqlAudit.Core.Models;

public sealed record ServiceWindowDecision(bool RequiresServiceWindow, string Reason);

public enum AuditOperationRisk
{
    MetadataOnly,
    StatisticsOnly,
    IndexReorganize,
    PotentiallyOnlineIndexBuild,
    OfflineIndexBuild,
    ConstraintValidation,
    Unknown
}

public static class ServiceWindowAdvisor
{
    public static ServiceWindowDecision ForConservativePolicy(AuditOperationRisk operationRisk, string reason)
    {
        var requiresWindow = operationRisk switch
        {
            AuditOperationRisk.MetadataOnly => false,
            AuditOperationRisk.StatisticsOnly => false,
            AuditOperationRisk.IndexReorganize => true,
            AuditOperationRisk.PotentiallyOnlineIndexBuild => true,
            AuditOperationRisk.OfflineIndexBuild => true,
            AuditOperationRisk.ConstraintValidation => true,
            _ => true
        };

        return new ServiceWindowDecision(requiresWindow, reason);
    }

    public static ServiceWindowDecision Yes(string reason) => new(true, reason);

    public static ServiceWindowDecision No(string reason) => new(false, reason);
}
