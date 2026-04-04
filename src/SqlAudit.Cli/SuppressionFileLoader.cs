using SqlAudit.Core.Models;
using System.Globalization;
using System.Text.Json;

namespace SqlAudit.Cli;

internal static class SuppressionFileLoader
{
    public const string DefaultSuppressionsFileName = "sqlaudit.suppressions.json";

    public static IReadOnlyList<AuditSuppressionRule> Load(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return [];
        }

        var parsed = Parse(Path.GetFullPath(filePath), strict: true);
        return parsed.Rules;
    }

    public static SuppressionValidationResult Validate(string filePath)
    {
        var parsed = Parse(Path.GetFullPath(filePath), strict: false);
        return new SuppressionValidationResult(
            parsed.Errors.Count == 0,
            parsed.Rules.Count,
            parsed.Errors,
            parsed.Warnings);
    }

    private static ParsedSuppressions Parse(string fullPath, bool strict)
    {
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Suppressions file not found: {fullPath}");
        }

        var json = File.ReadAllText(fullPath);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        var errors = new List<string>();
        var warnings = new List<string>();
        var rules = new List<AuditSuppressionRule>();

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Root element must be an object.");
            return ThrowIfStrict(fullPath, strict, rules, errors, warnings);
        }

        if (!document.RootElement.TryGetProperty("rules", out var rulesElement))
        {
            warnings.Add("No 'rules' property found. File is valid but has no suppressions.");
            return new ParsedSuppressions(rules, errors, warnings);
        }

        if (rulesElement.ValueKind != JsonValueKind.Array)
        {
            errors.Add("Property 'rules' must be an array.");
            return ThrowIfStrict(fullPath, strict, rules, errors, warnings);
        }

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < rulesElement.GetArrayLength(); i++)
        {
            var element = rulesElement[i];
            var location = $"rules[{i}]";

            if (element.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{location} must be an object.");
                continue;
            }

            if (!TryGetString(element, "findingId", out var findingId) || string.IsNullOrWhiteSpace(findingId))
            {
                errors.Add($"{location}.findingId is required and must be a non-empty string.");
                continue;
            }

            if (!TryGetOptionalString(element, "databaseObjectPattern", out var objectPattern, out var objectPatternError))
            {
                errors.Add($"{location}.databaseObjectPattern {objectPatternError}");
                continue;
            }

            if (!TryGetOptionalString(element, "reason", out var reason, out var reasonError))
            {
                errors.Add($"{location}.reason {reasonError}");
                continue;
            }

            if (!TryGetOptionalDateTimeOffset(element, "expiresUtc", out var expiresUtc, out var expiresError))
            {
                errors.Add($"{location}.expiresUtc {expiresError}");
                continue;
            }

            if (expiresUtc <= now)
            {
                warnings.Add($"{location} is expired and will not suppress findings.");
            }

            rules.Add(new AuditSuppressionRule(
                findingId.Trim(),
                string.IsNullOrWhiteSpace(objectPattern) ? null : objectPattern.Trim(),
                string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                expiresUtc));
        }

        return ThrowIfStrict(fullPath, strict, rules, errors, warnings);
    }

    private static ParsedSuppressions ThrowIfStrict(
        string fullPath,
        bool strict,
        IReadOnlyList<AuditSuppressionRule> rules,
#pragma warning disable CA1859 // Use concrete types when possible for improved performance
        IReadOnlyList<string> errors,
#pragma warning restore CA1859 // Use concrete types when possible for improved performance
        IReadOnlyList<string> warnings)
    {
        if (strict && errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Suppressions file '{fullPath}' has validation errors:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", errors)}");
        }

        return new ParsedSuppressions(rules, errors, warnings);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static bool TryGetOptionalString(JsonElement element, string propertyName, out string? value, out string? error)
    {
        value = null;
        error = null;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = "must be a string when provided.";
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static bool TryGetOptionalDateTimeOffset(
        JsonElement element,
        string propertyName,
        out DateTimeOffset? value,
        out string? error)
    {
        value = null;
        error = null;

        if (!element.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = "must be an ISO-8601 datetime string when provided.";
            return false;
        }

        var input = property.GetString();
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "must not be empty when provided.";
            return false;
        }

        if (!DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            error = "could not be parsed as a datetime.";
            return false;
        }

        value = parsed;
        return true;
    }

    private sealed record ParsedSuppressions(
        IReadOnlyList<AuditSuppressionRule> Rules,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings);
}

internal sealed record SuppressionValidationResult(
    bool IsValid,
    int RuleCount,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
