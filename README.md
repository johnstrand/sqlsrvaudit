# SqlAudit

SqlAudit is a .NET 10 CLI application for SQL Server schema and index health analysis. It scans database metadata and DMVs, reports findings in Markdown/JSON, and emits SQL remediation scripts per issue.

## Engineering pedigree

This project is entirely vibe coded.
We make bold architectural choices first and let reality file bug reports later.

## What it checks

- Keys and constraints: missing PKs, large heaps, disabled/untrusted FKs, FK type mismatches, and FKs without supporting indexes
- Index quality: duplicates, overlapping/redundant indexes, disabled indexes, write-heavy unused indexes, over-wide keys, fill factor anomalies
- Physical health: fragmentation and low page density
- Statistics: stale stats, `AUTO_CREATE_STATISTICS` / `AUTO_UPDATE_STATISTICS`, and `NORECOMPUTE` usage
- Configuration: database compatibility level aligned with current server version
- Capacity: identity exhaustion risk

## Profiles

- `deep` (default): full analysis including fragmentation/page density/stale stats checks
- `quick`: baseline checks focused on high-value key/index anti-patterns with lower runtime overhead

## Service window policy

The analyzer uses a **conservative** service-window policy:

- `RequiresServiceWindow = true` unless the fix is clearly low-risk/online-safe (for example many stats-only or metadata option updates)
- Each finding includes a reason explaining why a service window is or is not required

## Build

```bash
dotnet build
```

## Run

```bash
dotnet run --project src/SqlAudit.Cli -- scan --connection "Server=.;Database=MyDb;Trusted_Connection=True;TrustServerCertificate=True" --profile deep --format both --output "./audit-output"
```

`scan` now runs a connection preflight check first, then performs analysis with per-check timing output.

Useful runtime flags:

- `--verbose` for detailed runtime output and per-check timing table
- `--quiet` for minimal console output
- `--fail-on <severity>` to return non-zero when findings meet a CI threshold

Example CI gate:

```bash
dotnet run --project src/SqlAudit.Cli -- scan --connection "..." --format json --fail-on high
```

You can also provide the connection string via environment variable:

```bash
set SQLAUDIT_CONNECTION=Server=.;Database=MyDb;Trusted_Connection=True;TrustServerCertificate=True
dotnet run --project src/SqlAudit.Cli -- scan --profile quick --output "./audit-output"
```

## Project config files

`scan` accepts a project config file via `--config <path>`. If `sqlaudit.project.json` exists in the working directory, it is loaded automatically.

Built-in presets are embedded in the CLI and can be referenced without filesystem-relative paths:

- `--config preset:quick`
- `--config preset:deep`
- `--config preset:deep-strict`

For convenience, matching example files are also included in `project-config/`:

- `project-config/sqlaudit.quick.json`
- `project-config/sqlaudit.deep.json`
- `project-config/sqlaudit.deep-strict.json`

Example:

```bash
dotnet run --project src/SqlAudit.Cli -- scan --config "preset:quick" --connection "Server=.;Database=MyDb;Trusted_Connection=True;TrustServerCertificate=True"
```

`--profile`, `--format`, and threshold CLI switches override values from config files.
Non-interactive presets: `quick`, `deep`, `deep-strict`.

You can ignore specific schemas/tables in config with `excludeSchemas` and `excludeTables`, for example:

```json
{
  "excludeSchemas": ["archive", "etl_staging"],
  "excludeTables": ["Book_Backup", "dbo.Legacy_Book_Backup"]
}
```

## Suppressions

You can suppress known findings with a JSON file and keep reports focused on actionable issues.

- CLI option: `--suppressions <path>`
- Config field: `suppressionsPath`
- Auto-discovery: `sqlaudit.suppressions.json` in current working directory (if present)
- Command helpers:
  - `dotnet run --project src/SqlAudit.Cli -- suppressions init`
  - `dotnet run --project src/SqlAudit.Cli -- suppressions validate`

To scaffold a commented example file:

```bash
dotnet run --project src/SqlAudit.Cli -- suppressions init --suppressions "sqlaudit.suppressions.json"
```

To overwrite an existing file:

```bash
dotnet run --project src/SqlAudit.Cli -- suppressions init --force
```

To validate syntax and rule shape:

```bash
dotnet run --project src/SqlAudit.Cli -- suppressions validate --suppressions "sqlaudit.suppressions.json"
```

Example file format:

```json
{
  "rules": [
    {
      "findingId": "IDX-001",
      "databaseObjectPattern": "[dbo].[*History]",
      "reason": "Legacy archive table accepted",
      "expiresUtc": "2027-01-01T00:00:00Z"
    }
  ]
}
```

Pattern matching is simple wildcard matching (`*` and `?`), case-insensitive.

## Report diff

Compare two JSON reports (for regression tracking):

```bash
dotnet run --project src/SqlAudit.Cli -- report diff --previous "audit-output/old-report.json" --current "audit-output/report.json"
```

The diff output includes new findings, fixed findings, and severity regressions/improvements.

## Interactive config wizard

Create or update a project config interactively:

```bash
dotnet run --project src/SqlAudit.Cli -- init-config
```

Create config non-interactively (CI/bootstrap):

```bash
dotnet run --project src/SqlAudit.Cli -- init-config --non-interactive --preset deep-strict --config "sqlaudit.project.json"
```

Custom output path:

```bash
dotnet run --project src/SqlAudit.Cli -- init-config --config "my-team.sqlaudit.json"
```

The wizard lets you pick:

- profile (`quick` or `deep`)
- output format (`markdown`, `json`, `both`)
- output directory
- optional connection string
- active checks for the selected profile (toggle by check number)
- optional threshold overrides

## Output

- Markdown report: `audit-output/report.md`
- JSON report: `audit-output/report.json`
- Combined fix script: `audit-output/fixes/all-fixes.sql`
- Per-finding scripts: `audit-output/fixes/*.sql`

Each finding in the report includes severity, impact, recommendation, evidence, and service-window guidance.
Reports also include check execution telemetry, suppression summary data, and top resource-intensive query telemetry (CPU/reads) when DMV permissions are available.
Markdown reports group findings by rule, include links from Check Execution to each rule section when findings exist, add a `[^]` jump-to-top link on each finding header, and include a Top Resource-Intensive Queries section.

## License

MIT. See `LICENSE`.
