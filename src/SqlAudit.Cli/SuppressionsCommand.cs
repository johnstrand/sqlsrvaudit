namespace SqlAudit.Cli;

internal static class SuppressionsCommand
{
    public static int Run(CliOptions options)
    {
        var subcommand = options.Subcommand?.Trim();
        if (string.IsNullOrWhiteSpace(subcommand))
        {
            ScanOutput.ShowError("Missing suppressions subcommand.", hint: "Use 'init' or 'validate'.");
            return 2;
        }

        var path = ResolvePath(options.SuppressionsPath);

        return subcommand.ToLowerInvariant() switch
        {
            "init" => RunInit(path, options.Force),
            "validate" => RunValidate(path),
            _ => RunUnknownSubcommand(subcommand),
        };
    }

    private static int RunInit(string path, bool force)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) && !force)
        {
            ScanOutput.ShowError($"Suppressions file already exists: {fullPath}", hint: "Use --force to overwrite it.");
            return 2;
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, SampleSuppressionsContent + Environment.NewLine);
        ScanOutput.ShowSuccess($"Created suppressions file: {fullPath}");
        ScanOutput.ShowInfo($"Run validation with: suppressions validate --suppressions \"{fullPath}\"");
        return 0;
    }

    private static int RunValidate(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var result = SuppressionFileLoader.Validate(fullPath);

        ScanOutput.PrintSuppressionsValidationResult(fullPath, result);

        return result.IsValid ? 0 : 2;
    }

    private static int RunUnknownSubcommand(string subcommand)
    {
        ScanOutput.ShowError($"Unknown suppressions subcommand: {subcommand}", hint: "Use 'suppressions init' or 'suppressions validate'.");
        return 2;
    }

    private static string ResolvePath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        return SuppressionFileLoader.DefaultSuppressionsFileName;
    }

    private const string SampleSuppressionsContent = """
        {
          // Each rule suppresses matching findings.
          // Matching is case-insensitive and supports wildcards in databaseObjectPattern:
          //   *  = zero or more characters
          //   ?  = exactly one character
          "rules": [
            {
              "findingId": "IDX-001",
              "databaseObjectPattern": "[dbo].[*History]",
              "reason": "Accepted for archive workloads",
              "expiresUtc": "2027-01-01T00:00:00Z"
            },
            {
              "findingId": "STAT-002-AUTO-CREATE",
              "reason": "Managed manually in this environment"
            }
          ]
        }
        """;
}
