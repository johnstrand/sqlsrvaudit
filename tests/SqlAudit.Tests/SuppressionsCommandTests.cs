using SqlAudit.Cli;

namespace SqlAudit.Tests;

public sealed class SuppressionsCommandTests
{
    [Fact]
    public void InitThenValidate_Succeeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SqlAuditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var filePath = Path.Combine(tempDir, "sqlaudit.suppressions.json");
            var initOptions = CreateSuppressionsOptions("init", filePath, force: false);

            var initExit = SuppressionsCommand.Run(initOptions);
            Assert.Equal(0, initExit);
            Assert.True(File.Exists(filePath));

            var validateOptions = CreateSuppressionsOptions("validate", filePath, force: false);
            var validateExit = SuppressionsCommand.Run(validateOptions);
            Assert.Equal(0, validateExit);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Validate_InvalidFile_Fails()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SqlAuditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var filePath = Path.Combine(tempDir, "bad.suppressions.json");
            File.WriteAllText(filePath, "{\"rules\":[{\"findingId\":\"\"}]}");

            var validateOptions = CreateSuppressionsOptions("validate", filePath, force: false);
            var exit = SuppressionsCommand.Run(validateOptions);

            Assert.Equal(2, exit);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Init_ExistingFileWithoutForce_Fails()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SqlAuditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var filePath = Path.Combine(tempDir, "existing.suppressions.json");
            File.WriteAllText(filePath, "{\"rules\":[]}");

            var initOptions = CreateSuppressionsOptions("init", filePath, force: false);
            var exit = SuppressionsCommand.Run(initOptions);

            Assert.Equal(2, exit);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static CliOptions CreateSuppressionsOptions(string subcommand, string path, bool force)
    {
        return new CliOptions
        {
            Command = "suppressions",
            Subcommand = subcommand,
            ConnectionString = null,
            ConfigPath = null,
            OutputDirectory = null,
            MarkdownPath = null,
            JsonPath = null,
            FixesDirectory = null,
            SuppressionsPath = path,
            Profile = null,
            OutputFormat = null,
            NonInteractive = false,
            Force = force,
            Preset = null,
            ActiveCheckIds = null,
            AuditOptionOverrides = new AuditOptionsOverrides()
        };
    }
}
