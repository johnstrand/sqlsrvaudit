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
        Assert.Equal(10, quick.Count);
        Assert.Equal(16, deep.Count);
    }
}
