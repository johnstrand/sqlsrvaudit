using SqlAudit.SqlServer;

namespace SqlAudit.Tests;

public sealed class HealthCheckProfileTests
{
    [Fact]
    public void QuickProfile_HasFewerChecksThanDeep()
    {
        var quick = SqlServerHealthChecks.CreateQuick();
        var deep = SqlServerHealthChecks.CreateDeep();

        Assert.True(quick.Count < deep.Count);
        Assert.Equal(28, quick.Count);
        Assert.Equal(36, deep.Count);

        Assert.Contains(quick, check => string.Equals(check.Id, "CFG-001", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(deep, check => string.Equals(check.Id, "CFG-001", StringComparison.OrdinalIgnoreCase));
    }
}
