namespace SqlAudit.Cli;

internal sealed record ResolveRunOptionsResult(bool Success, EffectiveRunOptions? Options, int ExitCode)
{
    public static ResolveRunOptionsResult Ok(EffectiveRunOptions options) => new(Success: true, options, 0);

    public static ResolveRunOptionsResult Fail(int exitCode) => new(Success: false, Options: null, exitCode);
}
