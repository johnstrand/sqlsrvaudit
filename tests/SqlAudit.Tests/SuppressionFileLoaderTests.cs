using SqlAudit.Cli;
using System.IO;
using Xunit;
using System;

namespace SqlAudit.Tests;

public sealed class SuppressionFileLoaderTests
{
    [Fact]
    public void Load_NonExistentFile_ThrowsFileNotFoundException()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "doesnotexist.json");

        var ex = Assert.Throws<FileNotFoundException>(() => SuppressionFileLoader.Load(nonExistentPath));
        Assert.Contains("Suppressions file not found", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nonExistentPath, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NonExistentFile_ThrowsFileNotFoundException()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "doesnotexist.json");

        var ex = Assert.Throws<FileNotFoundException>(() => SuppressionFileLoader.Validate(nonExistentPath));
        Assert.Contains("Suppressions file not found", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nonExistentPath, ex.Message, StringComparison.Ordinal);
    }
}
