using SqlAudit.Core.Models;
using SqlAudit.SqlServer;
using System.Globalization;

namespace SqlAudit.Cli;

internal static class InteractiveConfigWizard
{
    public static int Run(CliOptions options)
    {
        if (options.NonInteractive)
        {
            var targetPath = ResolveTargetPath(options.ConfigPath, options.ProjectName);
            return RunNonInteractive(targetPath, options.Preset ?? ConfigPreset.Deep, options.ProjectName);
        }

        return RunInteractive(options.ConfigPath, options.ProjectName, options.Preset);
    }

    private static string ResolveTargetPath(string? explicitConfigPath, string? projectName)
    {
        if (!string.IsNullOrWhiteSpace(explicitConfigPath))
        {
            return explicitConfigPath;
        }

        if (!string.IsNullOrWhiteSpace(projectName))
        {
            return SlugifyName(projectName) + ".sqlaudit.json";
        }

        return ProjectConfigurationResolver.ResolveConfigPath(explicitPath: null);
    }

    private static int RunNonInteractive(string targetPath, ConfigPreset preset, string? projectName)
    {
        var config = ConfigPresetFactory.Create(preset, projectName);
        ProjectConfigurationResolver.SaveConfig(targetPath, config);

        Console.WriteLine("Config saved (non-interactive).");
        Console.WriteLine($"- Path: {Path.GetFullPath(targetPath)}");
        Console.WriteLine($"- Preset: {PresetName(preset)}");
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            Console.WriteLine($"- Project: {projectName}");
        }

        return 0;
    }

    private static int RunInteractive(string? explicitConfigPath, string? nameFromFlag, ConfigPreset? preset)
    {
        Console.WriteLine("SqlAudit interactive config wizard");
        Console.WriteLine();

        // Load initial defaults from whatever path is currently resolvable.
        var initialPath = ProjectConfigurationResolver.ResolveConfigPath(explicitConfigPath);
        var existing = ProjectConfigurationResolver.TryLoad(initialPath)
            ?? (preset.HasValue ? ConfigPresetFactory.Create(preset.Value) : null);

        // --name flag skips the prompt; otherwise ask interactively.
        var projectName = !string.IsNullOrWhiteSpace(nameFromFlag)
            ? nameFromFlag
            : PromptProjectName(existing?.ProjectName);

        string targetPath;
        if (!string.IsNullOrWhiteSpace(explicitConfigPath))
        {
            targetPath = explicitConfigPath;
        }
        else
        {
            targetPath = SlugifyName(projectName) + ".sqlaudit.json";

            // If the derived path differs from the initial default, try loading
            // existing config from the new path so defaults are project-specific.
            if (!string.Equals(targetPath, initialPath, StringComparison.OrdinalIgnoreCase))
            {
                existing = ProjectConfigurationResolver.TryLoad(targetPath) ?? existing;
            }
        }

        Console.WriteLine($"Target file: {Path.GetFullPath(targetPath)}");
        if (preset.HasValue)
        {
            Console.WriteLine($"Starting from preset: {PresetName(preset.Value)}");
        }

        Console.WriteLine();

        var profile = PromptProfile(existing?.Profile ?? AuditProfile.Deep);
        var format = PromptFormat(existing?.OutputFormat ?? OutputFormat.Both);
        var defaultOutput = existing?.OutputDirectory ?? $"audit-output/{profile.ToString().ToLowerInvariant()}";
        var outputDirectory = PromptString("Output directory", defaultOutput, allowEmpty: false)!;

        var existingConnection = existing?.ConnectionString;
        var connectionString = PromptString("Connection string (leave blank to omit from config)", existingConnection, allowEmpty: true);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = null;
        }

        var checks = SqlServerHealthChecks.GetDescriptors(profile);
        var active = BuildDefaultActiveSet(existing?.ActiveCheckIds, checks);
        active = PromptCheckSelection(checks, active);

        var optionOverrides = PromptOptionsOverrides(existing?.AuditOptions);
        var storeOverrides = optionOverrides.HasValues() ? optionOverrides : null;

        var allByDefault = checks.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var useDefaultCheckSet = active.SetEquals(allByDefault);

        var config = new ProjectConfigFile
        {
            ProjectName = projectName,
            ConnectionString = connectionString,
            Profile = profile,
            OutputFormat = format,
            OutputDirectory = outputDirectory,
            MarkdownPath = existing?.MarkdownPath,
            JsonPath = existing?.JsonPath,
            FixesDirectory = existing?.FixesDirectory,
            SuppressionsPath = existing?.SuppressionsPath,
            ExcludeSchemas = existing?.ExcludeSchemas,
            ExcludeTables = existing?.ExcludeTables,
            ActiveCheckIds = useDefaultCheckSet
                ? null
                : checks.Where(c => active.Contains(c.Id)).Select(c => c.Id).ToArray(),
            AuditOptions = storeOverrides,
        };

        ProjectConfigurationResolver.SaveConfig(targetPath, config);

        Console.WriteLine();
        Console.WriteLine("Config saved.");
        Console.WriteLine($"- Path: {Path.GetFullPath(targetPath)}");
        Console.WriteLine($"- Project: {projectName}");
        Console.WriteLine($"- Profile: {profile}");
        Console.WriteLine($"- Format: {format}");
        Console.WriteLine($"- Active checks: {(useDefaultCheckSet ? "default set" : active.Count.ToString(CultureInfo.InvariantCulture))}");

        return 0;
    }

    private static string PresetName(ConfigPreset preset) => preset switch
    {
        ConfigPreset.DeepStrict => "deep-strict",
        _ => preset.ToString().ToLowerInvariant(),
    };

    private static string PromptProjectName(string? existing)
    {
        return PromptString("Project name", existing ?? "project", allowEmpty: false)!;
    }

    private static string SlugifyName(string name)
    {
        var sb = new System.Text.StringBuilder();
        var lastWasHyphen = false;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '.' || ch == '_')
            {
                sb.Append(ch);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen && sb.Length > 0)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }

        while (sb.Length > 0 && sb[^1] == '-')
        {
            sb.Remove(sb.Length - 1, 1);
        }

        return sb.Length == 0 ? "project" : sb.ToString();
    }

    private static HashSet<string> BuildDefaultActiveSet(IReadOnlyList<string>? configuredIds, IReadOnlyList<CheckDescriptor> checks)
    {
        var available = checks.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (configuredIds is null || configuredIds.Count == 0)
        {
            return available;
        }

        var selected = configuredIds
            .Where(available.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return selected.Count == 0 ? available : selected;
    }

    private static HashSet<string> PromptCheckSelection(IReadOnlyList<CheckDescriptor> checks, HashSet<string> active)
    {
        Console.WriteLine();
        Console.WriteLine("Check selection");
        Console.WriteLine("Press Enter to keep defaults, or type 'custom' to choose individual checks.");
        Console.Write("Customize checks? [default: keep] > ");
        var mode = Console.ReadLine();
        if (!string.Equals(mode, "custom", StringComparison.OrdinalIgnoreCase))
        {
            return active;
        }

        while (true)
        {
            PrintCheckSelectionMenu(checks, active);
            var input = ReadSelectionInput();
            if (TryFinalizeSelection(input, active, out var completed))
            {
                if (completed)
                {
                    return active;
                }

                continue;
            }

            if (TryApplySelectionCommand(input, checks, ref active))
            {
                continue;
            }

            ToggleNumbers(input, checks, active);
        }
    }

    private static void PrintCheckSelectionMenu(IReadOnlyList<CheckDescriptor> checks, HashSet<string> active)
    {
        Console.WriteLine();
        for (var i = 0; i < checks.Count; i++)
        {
            var check = checks[i];
            var marker = active.Contains(check.Id) ? "x" : " ";
            Console.WriteLine($"{i + 1,2}. [{marker}] {check.Id} {check.Title} ({check.Category})");
        }

        Console.WriteLine();
        Console.WriteLine("Commands: done | all | none | toggle <n,n,...>");
    }

    private static string ReadSelectionInput()
    {
        Console.Write("> ");
        return (Console.ReadLine() ?? string.Empty).Trim();
    }

    private static bool TryFinalizeSelection(string input, HashSet<string> active, out bool completed)
    {
        completed = false;
        if (!string.Equals(input, "done", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (active.Count == 0)
        {
            Console.WriteLine("At least one check must remain active.");
            return true;
        }

        completed = true;
        return true;
    }

    private static bool TryApplySelectionCommand(string input, IReadOnlyList<CheckDescriptor> checks, ref HashSet<string> active)
    {
        if (string.Equals(input, "all", StringComparison.OrdinalIgnoreCase))
        {
            active = checks.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return true;
        }

        if (string.Equals(input, "none", StringComparison.OrdinalIgnoreCase))
        {
            active.Clear();
            return true;
        }

        if (!input.StartsWith("toggle", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ToggleNumbers(input.Length > 6 ? input[6..] : string.Empty, checks, active);
        return true;
    }

    private static void ToggleNumbers(string input, IReadOnlyList<CheckDescriptor> checks, HashSet<string> active)
    {
        var tokens = input
            .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            Console.WriteLine("No check numbers provided.");
            return;
        }

        foreach (var token in tokens)
        {
            if (!TryResolveCheckId(token, checks, out var id))
            {
                Console.WriteLine($"Invalid check number: {token}");
                continue;
            }

            if (!active.Add(id))
            {
                active.Remove(id);
            }
        }
    }

    private static bool TryResolveCheckId(string token, IReadOnlyList<CheckDescriptor> checks, out string id)
    {
        id = string.Empty;
        if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            || number < 1
            || number > checks.Count)
        {
            return false;
        }

        id = checks[number - 1].Id;
        return true;
    }

    private static AuditOptionsOverrides PromptOptionsOverrides(AuditOptionsOverrides? existing)
    {
        var result = new AuditOptionsOverrides
        {
            LargeTableRowThreshold = existing?.LargeTableRowThreshold,
            UnusedIndexMinUpdates = existing?.UnusedIndexMinUpdates,
            UnusedIndexMaxReads = existing?.UnusedIndexMaxReads,
            FragmentationMinPageCount = existing?.FragmentationMinPageCount,
            FragmentationReorganizeThresholdPercent = existing?.FragmentationReorganizeThresholdPercent,
            FragmentationRebuildThresholdPercent = existing?.FragmentationRebuildThresholdPercent,
            LowPageDensityThresholdPercent = existing?.LowPageDensityThresholdPercent,
            StaleStatsModificationPercent = existing?.StaleStatsModificationPercent,
            StaleStatsMinModifications = existing?.StaleStatsMinModifications,
            IdentityUsageWarningPercent = existing?.IdentityUsageWarningPercent,
            IdentityUsageCriticalPercent = existing?.IdentityUsageCriticalPercent,
        };

        Console.WriteLine();
        Console.Write("Customize threshold overrides? [y/N] > ");
        var input = Console.ReadLine();
        if (!IsYes(input))
        {
            return result;
        }

        result.LargeTableRowThreshold = PromptLong("LargeTableRowThreshold", result.LargeTableRowThreshold);
        result.UnusedIndexMinUpdates = PromptLong("UnusedIndexMinUpdates", result.UnusedIndexMinUpdates);
        result.UnusedIndexMaxReads = PromptLong("UnusedIndexMaxReads", result.UnusedIndexMaxReads);
        result.FragmentationMinPageCount = PromptInt("FragmentationMinPageCount", result.FragmentationMinPageCount);
        result.FragmentationReorganizeThresholdPercent = PromptDouble("FragmentationReorganizeThresholdPercent", result.FragmentationReorganizeThresholdPercent);
        result.FragmentationRebuildThresholdPercent = PromptDouble("FragmentationRebuildThresholdPercent", result.FragmentationRebuildThresholdPercent);
        result.LowPageDensityThresholdPercent = PromptDouble("LowPageDensityThresholdPercent", result.LowPageDensityThresholdPercent);
        result.StaleStatsModificationPercent = PromptDouble("StaleStatsModificationPercent", result.StaleStatsModificationPercent);
        result.StaleStatsMinModifications = PromptLong("StaleStatsMinModifications", result.StaleStatsMinModifications);
        result.IdentityUsageWarningPercent = PromptDouble("IdentityUsageWarningPercent", result.IdentityUsageWarningPercent);
        result.IdentityUsageCriticalPercent = PromptDouble("IdentityUsageCriticalPercent", result.IdentityUsageCriticalPercent);

        return result;
    }

    private static AuditProfile PromptProfile(AuditProfile @default)
    {
        while (true)
        {
            Console.Write($"Profile [quick/deep] (default: {@default.ToString().ToLowerInvariant()}) > ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                return @default;
            }

            if (Enum.TryParse<AuditProfile>(input, ignoreCase: true, out var profile))
            {
                return profile;
            }

            Console.WriteLine("Please enter 'quick' or 'deep'.");
        }
    }

    private static OutputFormat PromptFormat(OutputFormat @default)
    {
        while (true)
        {
            Console.Write($"Output format [markdown/json/both] (default: {@default.ToString().ToLowerInvariant()}) > ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                return @default;
            }

            if (Enum.TryParse<OutputFormat>(input, ignoreCase: true, out var format))
            {
                return format;
            }

            Console.WriteLine("Please enter 'markdown', 'json', or 'both'.");
        }
    }

    private static string? PromptString(string label, string? @default, bool allowEmpty)
    {
        while (true)
        {
            Console.Write($"{label}{(@default is null ? string.Empty : $" (default: {@default})")} > ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
            {
                if (allowEmpty)
                {
                    return @default;
                }

                if (!string.IsNullOrWhiteSpace(@default))
                {
                    return @default;
                }

                Console.WriteLine("A value is required.");
                continue;
            }

            return input.Trim();
        }
    }

    private static long? PromptLong(string name, long? current)
    {
        while (true)
        {
            Console.Write($"{name} (blank keep{(current.HasValue ? $": {current.Value}" : ", unset")}) > ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                return current;
            }

            if (long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            Console.WriteLine("Enter a valid integer.");
        }
    }

    private static int? PromptInt(string name, int? current)
    {
        while (true)
        {
            Console.Write($"{name} (blank keep{(current.HasValue ? $": {current.Value}" : ", unset")}) > ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                return current;
            }

            if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            Console.WriteLine("Enter a valid integer.");
        }
    }

    private static double? PromptDouble(string name, double? current)
    {
        while (true)
        {
            Console.Write($"{name} (blank keep{(current.HasValue ? $": {current.Value.ToString(CultureInfo.InvariantCulture)}" : ", unset")}) > ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                return current;
            }

            if (double.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            Console.WriteLine("Enter a valid decimal number.");
        }
    }

    private static bool IsYes(string? input)
    {
        return string.Equals(input?.Trim(), "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(input?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class AuditOptionsOverridesExtensions
{
    public static bool HasValues(this AuditOptionsOverrides options)
    {
        return new[]
        {
            options.LargeTableRowThreshold.HasValue,
            options.UnusedIndexMinUpdates.HasValue,
            options.UnusedIndexMaxReads.HasValue,
            options.FragmentationMinPageCount.HasValue,
            options.FragmentationReorganizeThresholdPercent.HasValue,
            options.FragmentationRebuildThresholdPercent.HasValue,
            options.LowPageDensityThresholdPercent.HasValue,
            options.StaleStatsModificationPercent.HasValue,
            options.StaleStatsMinModifications.HasValue,
            options.IdentityUsageWarningPercent.HasValue,
            options.IdentityUsageCriticalPercent.HasValue,
        }.Any(static hasValue => hasValue);
    }
}
