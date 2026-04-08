# SqlAudit

SqlAudit is a .NET 10 CLI application for SQL Server schema and performance health analysis. It scans database metadata and DMVs, reports findings in Markdown/JSON, and emits SQL remediation scripts per issue.

## Engineering pedigree

This project is entirely vibe coded.
We make bold architectural choices first and let reality file bug reports later.

## 🔒 Read-only — your database is never modified

SqlAudit **only reads** from your SQL Server instance. It issues `SELECT` queries against system catalog views and DMVs. It never executes `INSERT`, `UPDATE`, `DELETE`, `DDL`, or any statement that modifies data or schema.

The SQL fix scripts it generates are written to your local disk for you to review and run manually at a time of your choosing — the tool itself will never apply them.

## What it checks

SqlAudit ships **43 checks in the Quick profile** and **52 in Deep**, spanning:

- **Keys and constraints** — missing PKs, large heaps, disabled/untrusted FKs, FK type mismatches, FKs without supporting indexes
- **Index quality** — duplicates, overlapping/redundant indexes, disabled indexes, write-heavy unused indexes, over-wide keys, fill factor anomalies, columnstore opportunities
- **Physical health** — fragmentation, low page density
- **Statistics** — stale stats, `AUTO_CREATE_STATISTICS` / `AUTO_UPDATE_STATISTICS`, `NORECOMPUTE` usage
- **Configuration** — compatibility level, `sp_configure` settings (max memory, MAXDOP, cost threshold), harmful trace flags
- **Capacity** — identity exhaustion risk
- **Runtime pressure** — top resource-intensive queries, wait-stat breakdown (dominant category, CPU signal-wait ratio), active blocking, deadlock summary
- **Optimizer opportunities** — Query Store regressions, guarded missing-index signals
- **Operational posture** — log/VLF health, log reuse wait, backup recency (full/differential/log), tempdb usage, file autogrowth settings
- **Database options** — AUTO_SHRINK, AUTO_CLOSE, PAGE_VERIFY, RCSI, Query Store state
- **Storage** — low free disk space, data and log files on the same volume
- **Maintenance** — SQL Agent job failures (last 7 days)
- **Memory/IO** — memory pressure indicators, file IO latency
- **Compression** — large uncompressed tables
- **Plan cache** — plan cache pollution indicators
- **Security hygiene** — orphan users, `db_owner` membership, risky `public` grants
- **Growth trend** — cross-run table growth forecasting when a prior `data-model.json` exists
- **Column schema** — nullable columns with no NULL values, oversized string column declarations

## Profiles

- `deep` (default): full analysis — 52 checks including fragmentation, page density, stale stats, columnstore opportunities, and all advanced DMV-based checks
- `quick`: 43 checks focused on high-value key/index anti-patterns and operational posture with lower runtime overhead

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
- `--output-data-model` to also emit full filtered metadata snapshot JSON

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
  "outputDataModel": true,
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
dotnet run --project src/SqlAudit.Cli -- init-config --non-interactive --preset deep-strict --name my-project
```

Custom output path (overrides name-based file derivation):

```bash
dotnet run --project src/SqlAudit.Cli -- init-config --config "my-team.sqlaudit.json"
```

The wizard first asks for a **project name**. The name is slugified and used to
name the output file — for example, a project named `"My App"` produces
`my-app.sqlaudit.json`. Pass `--name <value>` to supply the name without being
prompted. If `--config` is given explicitly it takes precedence over the
name-derived path.

The wizard also lets you pick:

- profile (`quick` or `deep`)
- output format (`markdown`, `json`, `both`)
- output directory
- optional connection string
- active checks for the selected profile (toggle by check number)
- optional threshold overrides

## Output

- Markdown report: `audit-output/report.md`
- JSON report: `audit-output/report.json`
- Full data model JSON (optional): `audit-output/data-model.json`
- Combined fix script: `audit-output/fixes/all-fixes.sql`
- Per-finding scripts (no service window required): `audit-output/fixes/no-window/*.sql`
- Per-finding scripts (service window required): `audit-output/fixes/requires-window/*.sql`

Each finding in the report includes severity, impact, recommendation, evidence, and service-window guidance.
Reports also include check execution telemetry, suppression summary data, and top resource-intensive query telemetry (CPU/reads) when DMV permissions are available.
Markdown reports group findings by rule, include links from Check Execution to each rule section when findings exist, add a `[^]` jump-to-top link on each finding header, and include a Top Resource-Intensive Queries section.

## License

MIT. See `LICENSE`.
