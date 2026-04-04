$projectPath = Join-Path $PSScriptRoot "src/SqlAudit.Cli"

dotnet run --project $projectPath -- @args
exit $LASTEXITCODE
