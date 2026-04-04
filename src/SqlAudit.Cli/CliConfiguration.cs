using SqlAudit.Core.Models;
using SqlAudit.SqlServer;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlAudit.Cli;

internal enum OutputFormat
{
    Markdown,
    Json,
    Both,
}

internal enum LogVerbosity
{
    Quiet,
    Normal,
    Verbose,
}

internal enum ConfigPreset
{
    Quick,
    Deep,
    DeepStrict,
}

internal sealed class CliOptions
{
    public required string Command { get; init; }

    public string? Subcommand { get; init; }

    public string? PreviousReportPath { get; init; }

    public string? CurrentReportPath { get; init; }

    public string? ConnectionString { get; init; }

    public string? ConfigPath { get; init; }

    public string? OutputDirectory { get; init; }

    public string? MarkdownPath { get; init; }

    public string? JsonPath { get; init; }

    public string? FixesDirectory { get; init; }

    public string? SuppressionsPath { get; init; }

    public AuditProfile? Profile { get; init; }

    public OutputFormat? OutputFormat { get; init; }

    public bool NonInteractive { get; init; }

    public bool Force { get; init; }

    public LogVerbosity Verbosity { get; init; } = LogVerbosity.Normal;

    public ConfigPreset? Preset { get; init; }

    public AuditSeverity? FailOnSeverity { get; init; }

    public IReadOnlyList<string>? ActiveCheckIds { get; init; }

    public required AuditOptionsOverrides AuditOptionOverrides { get; init; }

    public static void PrintHelp()
    {
        var parser = BuildParser();
        parser.Root.Parse(["--help"]).Invoke();
    }

    public static ParseResult TryParse(string[] args)
    {
        if (args.Length > 0
            && string.Equals(args[0], "suppressions", StringComparison.OrdinalIgnoreCase)
            && (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal)))
        {
            return ParseResult.Fail("Missing suppressions subcommand. Use 'init' or 'validate'.");
        }

        if (args.Length > 0
            && string.Equals(args[0], "report", StringComparison.OrdinalIgnoreCase)
            && (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal)))
        {
            return ParseResult.Fail("Missing report subcommand. Use 'diff'.");
        }

        var parser = BuildParser();

        var parseResult = parser.Root.Parse(args);
        if (string.Equals(parseResult.Action?.GetType().Name, "HelpAction", StringComparison.Ordinal)
            || string.Equals(parseResult.Action?.GetType().Name, "VersionOptionAction", StringComparison.Ordinal)
            || string.Equals(parseResult.Action?.GetType().Name, "VersionAction", StringComparison.Ordinal))
        {
            parseResult.Invoke();
            return ParseResult.Help();
        }

        if (parseResult.Errors.Count > 0)
        {
            var errorMessage = string.Join(Environment.NewLine, parseResult.Errors.Select(e => e.Message));
            return ParseResult.Fail(errorMessage);
        }

        if (parseResult.GetValue(parser.Verbose) && parseResult.GetValue(parser.Quiet))
        {
            return ParseResult.Fail("--verbose and --quiet cannot be used together.");
        }

        string? command;
        string? subcommand = null;
        if (parseResult.GetResult(parser.Scan) is not null)
        {
            command = "scan";
        }
        else if (parseResult.GetResult(parser.InitConfig) is not null)
        {
            command = "init-config";
        }
        else if (parseResult.GetResult(parser.SuppressionsInit) is not null)
        {
            command = "suppressions";
            subcommand = "init";
        }
        else if (parseResult.GetResult(parser.SuppressionsValidate) is not null)
        {
            command = "suppressions";
            subcommand = "validate";
        }
        else if (parseResult.GetResult(parser.Suppressions) is not null)
        {
            return ParseResult.Fail("Missing suppressions subcommand. Use 'init' or 'validate'.");
        }
        else if (parseResult.GetResult(parser.ReportDiff) is not null)
        {
            command = "report";
            subcommand = "diff";
        }
        else if (parseResult.GetResult(parser.Report) is not null)
        {
            return ParseResult.Fail("Missing report subcommand. Use 'diff'.");
        }
        else
        {
            return ParseResult.Fail("No command specified.");
        }

        LogVerbosity verbosity;
        if (parseResult.GetValue(parser.Verbose))
        {
            verbosity = LogVerbosity.Verbose;
        }
        else if (parseResult.GetValue(parser.Quiet))
        {
            verbosity = LogVerbosity.Quiet;
        }
        else
        {
            verbosity = LogVerbosity.Normal;
        }

        if (!TryParseProfile(parseResult.GetValue(parser.Profile), out var parsedProfile, out var profileError))
        {
            return ParseResult.Fail(profileError!);
        }

        if (!TryParseFormat(parseResult.GetValue(parser.Format), out var parsedFormat, out var formatError))
        {
            return ParseResult.Fail(formatError!);
        }

        if (!TryParsePreset(parseResult.GetValue(parser.Preset), out var parsedPreset, out var presetError))
        {
            return ParseResult.Fail(presetError!);
        }

        if (!TryParseFailOnSeverity(parseResult.GetValue(parser.FailOn), out var parsedFailOn, out var failOnError))
        {
            return ParseResult.Fail(failOnError!);
        }

        var suppressionsPath = parseResult.GetValue(parser.SuppressionsPathOption)
            ?? parseResult.GetValue(parser.PathAlias);

        var options = new CliOptions
        {
            Command = command,
            Subcommand = subcommand,
            PreviousReportPath = parseResult.GetValue(parser.Previous),
            CurrentReportPath = parseResult.GetValue(parser.Current),
            ConnectionString = parseResult.GetValue(parser.Connection),
            ConfigPath = parseResult.GetValue(parser.Config),
            OutputDirectory = parseResult.GetValue(parser.Output),
            MarkdownPath = parseResult.GetValue(parser.Markdown),
            JsonPath = parseResult.GetValue(parser.Json),
            FixesDirectory = parseResult.GetValue(parser.FixesDir),
            SuppressionsPath = suppressionsPath,
            Profile = parsedProfile,
            OutputFormat = parsedFormat,
            NonInteractive = parseResult.GetValue(parser.NonInteractive),
            Force = parseResult.GetValue(parser.Force),
            Verbosity = verbosity,
            Preset = parsedPreset,
            FailOnSeverity = parsedFailOn,
            ActiveCheckIds = ParseCheckIds(parseResult.GetValue(parser.Checks)),
            AuditOptionOverrides = new AuditOptionsOverrides
            {
                LargeTableRowThreshold = parseResult.GetValue(parser.LargeRows),
                UnusedIndexMinUpdates = parseResult.GetValue(parser.UnusedMinUpdates),
                UnusedIndexMaxReads = parseResult.GetValue(parser.UnusedMaxReads),
                FragmentationReorganizeThresholdPercent = parseResult.GetValue(parser.FragReorg),
                FragmentationRebuildThresholdPercent = parseResult.GetValue(parser.FragRebuild),
                StaleStatsModificationPercent = parseResult.GetValue(parser.StatsModPct),
                StaleStatsMinModifications = parseResult.GetValue(parser.StatsMinMods),
                IdentityUsageWarningPercent = parseResult.GetValue(parser.IdentityWarn),
                IdentityUsageCriticalPercent = parseResult.GetValue(parser.IdentityCritical),
            },
        };

        return ParseResult.Ok(options);
    }

    private static bool TryParseProfile(string? value, out AuditProfile? profile, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            profile = null;
            error = null;
            return true;
        }

        if (Enum.TryParse<AuditProfile>(value, ignoreCase: true, out var parsed))
        {
            profile = parsed;
            error = null;
            return true;
        }

        profile = null;
        error = $"Invalid profile: {value}. Allowed: quick, deep.";
        return false;
    }

    private static bool TryParseFormat(string? value, out OutputFormat? format, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            format = null;
            error = null;
            return true;
        }

        if (Enum.TryParse<OutputFormat>(value, ignoreCase: true, out var parsed))
        {
            format = parsed;
            error = null;
            return true;
        }

        format = null;
        error = $"Invalid format: {value}. Allowed: markdown, json, both.";
        return false;
    }

    private static IReadOnlyList<string>? ParseCheckIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return [.. value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase),];
    }

    private static bool TryParsePreset(string? value, out ConfigPreset? preset, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            preset = null;
            error = null;
            return true;
        }

        if (string.Equals(value, "deep-strict", StringComparison.OrdinalIgnoreCase))
        {
            preset = ConfigPreset.DeepStrict;
            error = null;
            return true;
        }

        if (Enum.TryParse<ConfigPreset>(value, ignoreCase: true, out var parsed))
        {
            preset = parsed;
            error = null;
            return true;
        }

        preset = null;
        error = $"Invalid preset: {value}. Allowed: quick, deep, deep-strict.";
        return false;
    }

    private static bool TryParseFailOnSeverity(string? value, out AuditSeverity? severity, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            severity = null;
            error = null;
            return true;
        }

        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            severity = null;
            error = null;
            return true;
        }

        if (Enum.TryParse<AuditSeverity>(value, ignoreCase: true, out var parsed))
        {
            severity = parsed;
            error = null;
            return true;
        }

        severity = null;
        error = $"Invalid fail-on severity: {value}. Allowed: none, critical, high, medium, low, info.";
        return false;
    }

    private static CliParser BuildParser()
    {
        var parser = new CliParser
        {
            Connection = CreateOption<string?>("--connection", "SQL Server connection string"),
            Config = CreateOption<string?>("--config", "Project config JSON path"),
            Profile = CreateOption<string?>("--profile", "Check profile: quick|deep"),
            Format = CreateOption<string?>("--format", "Report output format: markdown|json|both"),
            Checks = CreateOption<string?>("--checks", "Comma-separated check IDs"),
            Output = CreateOption<string?>("--output", "Output directory"),
            Markdown = CreateOption<string?>("--markdown", "Markdown report path"),
            Json = CreateOption<string?>("--json", "JSON report path"),
            FixesDir = CreateOption<string?>("--fixes-dir", "Folder for SQL fix scripts"),
            SuppressionsPathOption = CreateOption<string?>("--suppressions", "Suppressions JSON file path"),
            NonInteractive = CreateOption<bool>("--non-interactive", "Create config without prompts"),
            Force = CreateOption<bool>("--force", "Overwrite existing file"),
            Verbose = CreateOption<bool>("--verbose", "Show detailed runtime output"),
            Quiet = CreateOption<bool>("--quiet", "Show minimal runtime output"),
            Preset = CreateOption<string?>("--preset", "Config preset: quick|deep|deep-strict"),
            FailOn = CreateOption<string?>("--fail-on", "Exit non-zero if findings meet threshold"),
            Previous = CreateOption<string?>("--previous", "Baseline JSON report path"),
            Current = CreateOption<string?>("--current", "Current JSON report path"),
            PathAlias = CreateOption<string?>("--path", "Alias for --suppressions"),

            LargeRows = CreateOption<long?>("--large-table-rows", "Large table row threshold"),
            UnusedMinUpdates = CreateOption<long?>("--unused-index-min-updates", "Unused index minimum updates threshold"),
            UnusedMaxReads = CreateOption<long?>("--unused-index-max-reads", "Unused index maximum reads threshold"),
            FragReorg = CreateOption<double?>("--frag-reorg-pct", "Fragmentation reorganize threshold percent"),
            FragRebuild = CreateOption<double?>("--frag-rebuild-pct", "Fragmentation rebuild threshold percent"),
            StatsModPct = CreateOption<double?>("--stats-mod-pct", "Stale statistics modification percentage"),
            StatsMinMods = CreateOption<long?>("--stats-min-mods", "Stale statistics minimum modifications"),
            IdentityWarn = CreateOption<double?>("--identity-warn-pct", "Identity warning threshold percentage"),
            IdentityCritical = CreateOption<double?>("--identity-critical-pct", "Identity critical threshold percentage"),

            Scan = new Command("scan", "Run SQL Server health scan and generate reports/scripts."),
        };
        parser.Scan.Add(parser.Connection);
        parser.Scan.Add(parser.Config);
        parser.Scan.Add(parser.Profile);
        parser.Scan.Add(parser.Format);
        parser.Scan.Add(parser.Checks);
        parser.Scan.Add(parser.Output);
        parser.Scan.Add(parser.Markdown);
        parser.Scan.Add(parser.Json);
        parser.Scan.Add(parser.FixesDir);
        parser.Scan.Add(parser.SuppressionsPathOption);
        parser.Scan.Add(parser.FailOn);
        parser.Scan.Add(parser.Verbose);
        parser.Scan.Add(parser.Quiet);
        parser.Scan.Add(parser.LargeRows);
        parser.Scan.Add(parser.UnusedMinUpdates);
        parser.Scan.Add(parser.UnusedMaxReads);
        parser.Scan.Add(parser.FragReorg);
        parser.Scan.Add(parser.FragRebuild);
        parser.Scan.Add(parser.StatsModPct);
        parser.Scan.Add(parser.StatsMinMods);
        parser.Scan.Add(parser.IdentityWarn);
        parser.Scan.Add(parser.IdentityCritical);

        parser.InitConfig = new Command("init-config", "Create or update project configuration.")
        {
            parser.Config,
            parser.NonInteractive,
            parser.Preset,
        };

        parser.SuppressionsInit = new Command("init", "Create a suppression file template.")
        {
            parser.SuppressionsPathOption,
            parser.PathAlias,
            parser.Force,
        };

        parser.SuppressionsValidate = new Command("validate", "Validate suppression file syntax and rules.")
        {
            parser.SuppressionsPathOption,
            parser.PathAlias,
        };

        parser.Suppressions = new Command("suppressions", "Manage suppression files.")
        {
            parser.SuppressionsInit,
            parser.SuppressionsValidate,
        };

        parser.ReportDiff = new Command("diff", "Compare two JSON reports.")
        {
            parser.Previous,
            parser.Current,
            parser.Verbose,
            parser.Quiet,
        };

        parser.Report = new Command("report", "Report utilities.")
        {
            parser.ReportDiff,
        };

#pragma warning disable IDE0028 // Simplify collection initialization
        parser.Root = new RootCommand("SQL Server schema and index health audit tool.")
        {
            parser.Scan,
            parser.InitConfig,
            parser.Suppressions,
            parser.Report,
        };
#pragma warning restore IDE0028 // Simplify collection initialization

        return parser;
    }

    private static Option<T> CreateOption<T>(string alias, string description)
    {
        var option = new Option<T>(alias)
        {
            Description = description,
        };

        return option;
    }

    private sealed class CliParser
    {
        public RootCommand Root { get; set; } = null!;

        public Command Scan { get; set; } = null!;

        public Command InitConfig { get; set; } = null!;

        public Command Suppressions { get; set; } = null!;

        public Command SuppressionsInit { get; set; } = null!;

        public Command SuppressionsValidate { get; set; } = null!;

        public Command Report { get; set; } = null!;

        public Command ReportDiff { get; set; } = null!;

        public Option<string?> Connection { get; set; } = null!;

        public Option<string?> Config { get; set; } = null!;

        public Option<string?> Profile { get; set; } = null!;

        public Option<string?> Format { get; set; } = null!;

        public Option<string?> Checks { get; set; } = null!;

        public Option<string?> Output { get; set; } = null!;

        public Option<string?> Markdown { get; set; } = null!;

        public Option<string?> Json { get; set; } = null!;

        public Option<string?> FixesDir { get; set; } = null!;

        public Option<string?> SuppressionsPathOption { get; set; } = null!;

        public Option<bool> NonInteractive { get; set; } = null!;

        public Option<bool> Force { get; set; } = null!;

        public Option<bool> Verbose { get; set; } = null!;

        public Option<bool> Quiet { get; set; } = null!;

        public Option<string?> Preset { get; set; } = null!;

        public Option<string?> FailOn { get; set; } = null!;

        public Option<string?> Previous { get; set; } = null!;

        public Option<string?> Current { get; set; } = null!;

        public Option<string?> PathAlias { get; set; } = null!;

        public Option<long?> LargeRows { get; set; } = null!;

        public Option<long?> UnusedMinUpdates { get; set; } = null!;

        public Option<long?> UnusedMaxReads { get; set; } = null!;

        public Option<double?> FragReorg { get; set; } = null!;

        public Option<double?> FragRebuild { get; set; } = null!;

        public Option<double?> StatsModPct { get; set; } = null!;

        public Option<long?> StatsMinMods { get; set; } = null!;

        public Option<double?> IdentityWarn { get; set; } = null!;

        public Option<double?> IdentityCritical { get; set; } = null!;
    }
}

internal sealed record ParseResult(bool Success, CliOptions? Options, string? ErrorMessage)
{
    public static ParseResult Ok(CliOptions options) => new(Success: true, options, ErrorMessage: null);

    public static ParseResult Help() => new(Success: false, Options: null, ErrorMessage: null);

    public static ParseResult Fail(string error) => new(Success: false, Options: null, error);
}

internal sealed class AuditOptionsOverrides
{
    public long? LargeTableRowThreshold { get; set; }

    public long? UnusedIndexMinUpdates { get; set; }

    public long? UnusedIndexMaxReads { get; set; }

    public int? FragmentationMinPageCount { get; set; }

    public double? FragmentationReorganizeThresholdPercent { get; set; }

    public double? FragmentationRebuildThresholdPercent { get; set; }

    public double? LowPageDensityThresholdPercent { get; set; }

    public double? StaleStatsModificationPercent { get; set; }

    public long? StaleStatsMinModifications { get; set; }

    public double? IdentityUsageWarningPercent { get; set; }

    public double? IdentityUsageCriticalPercent { get; set; }

    public AuditOptions ApplyTo(AuditOptions baseline) => new()
    {
        LargeTableRowThreshold = LargeTableRowThreshold ?? baseline.LargeTableRowThreshold,
        UnusedIndexMinUpdates = UnusedIndexMinUpdates ?? baseline.UnusedIndexMinUpdates,
        UnusedIndexMaxReads = UnusedIndexMaxReads ?? baseline.UnusedIndexMaxReads,
        FragmentationMinPageCount = FragmentationMinPageCount ?? baseline.FragmentationMinPageCount,
        FragmentationReorganizeThresholdPercent = FragmentationReorganizeThresholdPercent ?? baseline.FragmentationReorganizeThresholdPercent,
        FragmentationRebuildThresholdPercent = FragmentationRebuildThresholdPercent ?? baseline.FragmentationRebuildThresholdPercent,
        LowPageDensityThresholdPercent = LowPageDensityThresholdPercent ?? baseline.LowPageDensityThresholdPercent,
        StaleStatsModificationPercent = StaleStatsModificationPercent ?? baseline.StaleStatsModificationPercent,
        StaleStatsMinModifications = StaleStatsMinModifications ?? baseline.StaleStatsMinModifications,
        IdentityUsageWarningPercent = IdentityUsageWarningPercent ?? baseline.IdentityUsageWarningPercent,
        IdentityUsageCriticalPercent = IdentityUsageCriticalPercent ?? baseline.IdentityUsageCriticalPercent,
    };
}

internal sealed class ProjectConfigFile
{
    public string? ConnectionString { get; init; }

    public AuditProfile? Profile { get; init; }

    public OutputFormat? OutputFormat { get; init; }

    public string? OutputDirectory { get; init; }

    public string? MarkdownPath { get; init; }

    public string? JsonPath { get; init; }

    public string? FixesDirectory { get; init; }

    public string? SuppressionsPath { get; init; }

    public IReadOnlyList<string>? ActiveCheckIds { get; init; }

    public AuditOptionsOverrides? AuditOptions { get; init; }
}

internal sealed class EffectiveRunOptions
{
    public required string ConnectionString { get; init; }

    public required AuditProfile Profile { get; init; }

    public required OutputFormat Format { get; init; }

    public required string OutputDirectory { get; init; }

    public required string MarkdownPath { get; init; }

    public required string JsonPath { get; init; }

    public required string FixesDirectory { get; init; }

    public required string? SuppressionsPath { get; init; }

    public required LogVerbosity Verbosity { get; init; }

    public required AuditSeverity? FailOnSeverity { get; init; }

    public required IReadOnlyList<string>? ActiveCheckIds { get; init; }

    public required AuditOptions AuditOptions { get; init; }
}

internal static class ProjectConfigurationResolver
{
    public static EffectiveRunOptions Resolve(CliOptions cliOptions, string? environmentConnectionString)
    {
        var configPath = ResolveConfigPath(cliOptions.ConfigPath);
        var config = LoadProjectConfig(configPath, required: !string.IsNullOrWhiteSpace(cliOptions.ConfigPath));

        var profile = cliOptions.Profile ?? config?.Profile ?? AuditProfile.Deep;
        var baseline = AuditProfileDefaults.For(profile);
        var fromFile = config?.AuditOptions?.ApplyTo(baseline) ?? baseline;
        var effectiveAuditOptions = cliOptions.AuditOptionOverrides.ApplyTo(fromFile);

        var connectionString = cliOptions.ConnectionString
            ?? config?.ConnectionString
            ?? environmentConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string is required. Use --connection, --config, or SQLAUDIT_CONNECTION.");
        }

        var activeCheckIds = cliOptions.ActiveCheckIds ?? config?.ActiveCheckIds;
        ValidateCheckIds(profile, activeCheckIds);

        var format = cliOptions.OutputFormat ?? config?.OutputFormat ?? OutputFormat.Both;
        var outputDir = Path.GetFullPath(cliOptions.OutputDirectory ?? config?.OutputDirectory ?? "audit-output");
        var suppressionsPath = ResolveSuppressionsPath(cliOptions.SuppressionsPath, config?.SuppressionsPath);

        return new EffectiveRunOptions
        {
            ConnectionString = connectionString,
            Profile = profile,
            Format = format,
            OutputDirectory = outputDir,
            MarkdownPath = Path.GetFullPath(cliOptions.MarkdownPath ?? config?.MarkdownPath ?? Path.Combine(outputDir, "report.md")),
            JsonPath = Path.GetFullPath(cliOptions.JsonPath ?? config?.JsonPath ?? Path.Combine(outputDir, "report.json")),
            FixesDirectory = Path.GetFullPath(cliOptions.FixesDirectory ?? config?.FixesDirectory ?? Path.Combine(outputDir, "fixes")),
            SuppressionsPath = suppressionsPath,
            Verbosity = cliOptions.Verbosity,
            FailOnSeverity = cliOptions.FailOnSeverity,
            ActiveCheckIds = activeCheckIds,
            AuditOptions = effectiveAuditOptions,
        };
    }

    public static ProjectConfigFile? TryLoad(string? configPath)
    {
        var path = ResolveConfigPath(configPath);
        return LoadProjectConfig(path, required: false);
    }

    public static string ResolveConfigPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        const string defaultName = "sqlaudit.project.json";
        return defaultName;
    }

    public static void SaveConfig(string path, ProjectConfigFile config)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = CreateSerializerOptions();
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        var json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(fullPath, json + Environment.NewLine);
    }

    private static ProjectConfigFile? LoadProjectConfig(string? configPath, bool required)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(configPath);
        if (!File.Exists(fullPath))
        {
            if (required)
            {
                throw new FileNotFoundException($"Config file not found: {fullPath}");
            }

            return null;
        }

        var json = File.ReadAllText(fullPath);

        var config = JsonSerializer.Deserialize<ProjectConfigFile>(json, CreateSerializerOptions())
            ?? throw new InvalidOperationException($"Unable to parse config file: {fullPath}");

        return config;
    }

    private static JsonSerializerOptions CreateSerializerOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static string? ResolveSuppressionsPath(string? cliPath, string? configPath)
    {
        if (!string.IsNullOrWhiteSpace(cliPath))
        {
            return Path.GetFullPath(cliPath);
        }

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            return Path.GetFullPath(configPath);
        }

        const string defaultSuppressions = SuppressionFileLoader.DefaultSuppressionsFileName;
        return File.Exists(defaultSuppressions) ? Path.GetFullPath(defaultSuppressions) : null;
    }

    private static void ValidateCheckIds(AuditProfile profile, IReadOnlyList<string>? activeCheckIds)
    {
        if (activeCheckIds is null || activeCheckIds.Count == 0)
        {
            return;
        }

        var valid = SqlServerHealthChecks.GetDescriptors(profile)
            .Select(c => c.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var invalid = activeCheckIds
            .Where(id => !valid.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (invalid.Length == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Invalid check id(s) for profile '{profile}': {string.Join(", ", invalid)}.");
    }
}
