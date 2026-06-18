using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SqlAudit.Core.Models;
using SqlAudit.SqlServer;
using Xunit;

namespace SqlAudit.Tests;

public sealed class SqlServerSnapshotCollectorTests
{
    [Fact]
    public async Task TryReadOptionalListAsync_CatchesSqlException_AddsWarningAndReturnsEmptyList()
    {
        // Arrange
        var warnings = new List<CollectionWarning>();
        var expectedMessage = "Test SQL Exception";
        var section = "TestSection";

        Func<Task<IReadOnlyList<string>>> failingRead = () =>
        {
            throw SqlExceptionHelper.CreateSqlException(expectedMessage);
        };

        // We have to use reflection because TryReadOptionalListAsync is private
        var methodInfo = typeof(SqlServerSnapshotCollector).GetMethod(
            "TryReadOptionalListAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        var genericMethod = methodInfo!.MakeGenericMethod(typeof(string));

        // Act
        var task = (Task<IReadOnlyList<string>>)genericMethod.Invoke(null, new object[] { failingRead, warnings, section })!;
        var result = await task;

        // Assert
        Assert.Empty(result);
        var warning = Assert.Single(warnings);
        Assert.Equal(section, warning.Section);
        Assert.Equal(expectedMessage, warning.Reason);
    }

    [Fact]
    public async Task TryReadOptionalAsync_CatchesSqlException_AddsWarningAndReturnsNull()
    {
        // Arrange
        var warnings = new List<CollectionWarning>();
        var expectedMessage = "Test SQL Exception Optional";
        var section = "TestSectionOptional";

        Func<Task<string?>> failingRead = () =>
        {
            throw SqlExceptionHelper.CreateSqlException(expectedMessage);
        };

        var methodInfo = typeof(SqlServerSnapshotCollector).GetMethod(
            "TryReadOptionalAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        var genericMethod = methodInfo!.MakeGenericMethod(typeof(string));

        // Act
        var task = (Task<string?>)genericMethod.Invoke(null, new object[] { failingRead, warnings, section })!;
        var result = await task;

        // Assert
        Assert.Null(result);
        var warning = Assert.Single(warnings);
        Assert.Equal(section, warning.Section);
        Assert.Equal(expectedMessage, warning.Reason);
    }

    [Fact]
    public async Task TryReadOptionalStructAsync_CatchesSqlException_AddsWarningAndReturnsNull()
    {
        // Arrange
        var warnings = new List<CollectionWarning>();
        var expectedMessage = "Test SQL Exception Struct";
        var section = "TestSectionStruct";

        Func<Task<int?>> failingRead = () =>
        {
            throw SqlExceptionHelper.CreateSqlException(expectedMessage);
        };

        var methodInfo = typeof(SqlServerSnapshotCollector).GetMethod(
            "TryReadOptionalStructAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        var genericMethod = methodInfo!.MakeGenericMethod(typeof(int));

        // Act
        var task = (Task<int?>)genericMethod.Invoke(null, new object[] { failingRead, warnings, section })!;
        var result = await task;

        // Assert
        Assert.Null(result);
        var warning = Assert.Single(warnings);
        Assert.Equal(section, warning.Section);
        Assert.Equal(expectedMessage, warning.Reason);
    }
}
