using SqlAudit.SqlServer;

namespace SqlAudit.Tests;

public sealed class SqlNameTests
{
    [Fact]
    public void Table_EscapesSchemaAndTableNames()
    {
        var table = SqlName.Table("d]bo", "Ord]ers");

        Assert.Equal("[d]]bo].[Ord]]ers]", table);
    }

    [Fact]
    public void IndexAndConstraint_EscapeNames()
    {
        Assert.Equal("[IX_Boo]]ks]", SqlName.Index("IX_Boo]ks"));
        Assert.Equal("[FK_Boo]]ks]", SqlName.Constraint("FK_Boo]ks"));
    }

    [Fact]
    public void ObjectNameSuffix_NormalizesCharactersAndTruncates()
    {
        var normalized = SqlName.ObjectNameSuffix("Book Backup-2024]");
        var longName = SqlName.ObjectNameSuffix(new string('a', 120));

        Assert.Equal("Book_Backup_2024", normalized);
        Assert.Equal(110, longName.Length);
    }
}
