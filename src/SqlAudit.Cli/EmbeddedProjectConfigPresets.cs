using System.Reflection;
using System.Text.Json;

namespace SqlAudit.Cli;

internal static class EmbeddedProjectConfigPresets
{
    private const string ResourcePrefix = "SqlAudit.Cli.ConfigPresets.";

    private static readonly IReadOnlyDictionary<string, string> ResourceFileNameByAlias =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["quick"] = "sqlaudit.quick.json",
            ["deep"] = "sqlaudit.deep.json",
            ["deep-strict"] = "sqlaudit.deep-strict.json",
            ["preset:quick"] = "sqlaudit.quick.json",
            ["preset:deep"] = "sqlaudit.deep.json",
            ["preset:deep-strict"] = "sqlaudit.deep-strict.json",
            ["sqlaudit.quick.json"] = "sqlaudit.quick.json",
            ["sqlaudit.deep.json"] = "sqlaudit.deep.json",
            ["sqlaudit.deep-strict.json"] = "sqlaudit.deep-strict.json",
        };

    public static bool TryLoad(string requestedPath, JsonSerializerOptions serializerOptions, out ProjectConfigFile? config)
    {
        config = null;
        if (!TryResolveResourceFileName(requestedPath, out var resourceFileName))
        {
            return false;
        }

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + resourceFileName);
        if (stream is null)
        {
            return false;
        }

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        config = JsonSerializer.Deserialize<ProjectConfigFile>(json, serializerOptions)
            ?? throw new InvalidOperationException($"Unable to parse embedded config preset '{requestedPath}'.");

        return true;
    }

    private static bool TryResolveResourceFileName(string requestedPath, out string resourceFileName)
    {
        resourceFileName = string.Empty;
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return false;
        }

        var normalized = requestedPath.Trim().Replace('\\', '/');
        if (ResourceFileNameByAlias.TryGetValue(normalized, out var aliasFileName)
            && !string.IsNullOrWhiteSpace(aliasFileName))
        {
            resourceFileName = aliasFileName;
            return true;
        }

        if (!normalized.StartsWith("project-config/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(fileName)
            || !ResourceFileNameByAlias.TryGetValue(fileName, out aliasFileName)
            || string.IsNullOrWhiteSpace(aliasFileName))
        {
            return false;
        }

        resourceFileName = aliasFileName;
        return true;
    }
}
