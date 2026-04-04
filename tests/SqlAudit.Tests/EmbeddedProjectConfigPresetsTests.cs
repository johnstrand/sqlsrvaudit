using System.Text.Json;
using System.Text.Json.Serialization;
using SqlAudit.Cli;
using SqlAudit.Core.Models;

namespace SqlAudit.Tests;

public sealed class EmbeddedProjectConfigPresetsTests
{
    [Fact]
    public void TryLoad_LoadsQuickPresetByAlias()
    {
        var success = EmbeddedProjectConfigPresets.TryLoad("preset:quick", CreateSerializerOptions(), out var config);

        Assert.True(success);
        Assert.NotNull(config);
        Assert.Equal(AuditProfile.Quick, config!.Profile);
        Assert.Equal(OutputFormat.Both, config.OutputFormat);
        Assert.Equal("audit-output/quick", config.OutputDirectory);
    }

    [Fact]
    public void TryLoad_LoadsDeepStrictFromProjectConfigPathAlias()
    {
        var success = EmbeddedProjectConfigPresets.TryLoad("project-config/sqlaudit.deep-strict.json", CreateSerializerOptions(), out var config);

        Assert.True(success);
        Assert.NotNull(config);
        Assert.Equal(AuditProfile.Deep, config!.Profile);
        Assert.Equal(500, config.AuditOptions!.FragmentationMinPageCount);
        Assert.Equal(20, config.AuditOptions.FragmentationRebuildThresholdPercent);
    }

    [Fact]
    public void TryLoad_ReturnsFalseForUnknownAlias()
    {
        var success = EmbeddedProjectConfigPresets.TryLoad("preset:turbo", CreateSerializerOptions(), out var config);

        Assert.False(success);
        Assert.Null(config);
    }

    private static JsonSerializerOptions CreateSerializerOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
