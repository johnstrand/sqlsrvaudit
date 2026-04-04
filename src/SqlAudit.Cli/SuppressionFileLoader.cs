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

        using var document = LoadDocument(fullPath);
        var errors = new List<string>();
        var warnings = new List<string>();
        var rules = new List<AuditSuppressionRule>();

        if (!TryGetRulesArray(document.RootElement, out var rulesElement, errors, warnings))
        {
            return ThrowIfStrict(fullPath, strict, rules, errors, warnings);
        }

        ParseRules(rulesElement, rules, errors, warnings);

        return ThrowIfStrict(fullPath, strict, rules, errors, warnings);
    }

    private static JsonDocument LoadDocument(string fullPath)
    {
        var json = File.ReadAllText(fullPath);
        return JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
    }

    private static bool TryGetRulesArray(
        JsonElement rootElement,
        out JsonElement rulesElement,
        List<string> errors,
        List<string> warnings)
    {
        rulesElement = default;

        if (rootElement.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Root element must be an object.");
            return false;
        }

        if (!rootElement.TryGetProperty("rules", out rulesElement))
        {
            warnings.Add("No 'rules' property found. File is valid but has no suppressions.");
            return false;
        }

        if (rulesElement.ValueKind != JsonValueKind.Array)
        {
            errors.Add("Property 'rules' must be an array.");
            return false;
        }

        return true;
    }

    private static void ParseRules(
        JsonElement rulesElement,
        List<AuditSuppressionRule> rules,
        List<string> errors,
        List<string> warnings)
    {
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < rulesElement.GetArrayLength(); i++)
        {
            var location = $"rules[{i}]";
            var parseResult = ParseRule(rulesElement[i], location, now);
            if (!parseResult.IsValid)
            {
                errors.Add(parseResult.ErrorMessage!);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(parseResult.WarningMessage))
            {
                warnings.Add(parseResult.WarningMessage!);
            }

            rules.Add(parseResult.Rule!);
        }
    }

    private static RuleParseResult ParseRule(JsonElement element, string location, DateTimeOffset now)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return RuleParseResult.Fail($"{location} must be an object.");
        }

        if (!TryGetString(element, "findingId", out var findingId) || string.IsNullOrWhiteSpace(findingId))
        {
            return RuleParseResult.Fail($"{location}.findingId is required and must be a non-empty string.");
        }

        if (!TryGetOptionalString(element, "databaseObjectPattern", out var objectPattern, out var objectPatternError))
        {
            return RuleParseResult.Fail($"{location}.databaseObjectPattern {objectPatternError}");
        }

        if (!TryGetOptionalString(element, "reason", out var reason, out var reasonError))
        {
            return RuleParseResult.Fail($"{location}.reason {reasonError}");
        }

        if (!TryGetOptionalDateTimeOffset(element, "expiresUtc", out var expiresUtc, out var expiresError))
        {
            return RuleParseResult.Fail($"{location}.expiresUtc {expiresError}");
        }

        var warning = expiresUtc <= now
            ? $"{location} is expired and will not suppress findings."
            : null;

        return RuleParseResult.Success(new AuditSuppressionRule(
            findingId.Trim(),
            string.IsNullOrWhiteSpace(objectPattern) ? null : objectPattern.Trim(),
            string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            expiresUtc),
            warning);
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

    private sealed record RuleParseResult(bool IsValid, AuditSuppressionRule? Rule, string? ErrorMessage, string? WarningMessage)
    {
        public static RuleParseResult Success(AuditSuppressionRule rule, string? warningMessage) => new(IsValid: true, rule, ErrorMessage: null, warningMessage);

        public static RuleParseResult Fail(string errorMessage) => new(IsValid: false, Rule: null, errorMessage, WarningMessage: null);
    }
}

internal sealed record SuppressionValidationResult(
    bool IsValid,
    int RuleCount,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
