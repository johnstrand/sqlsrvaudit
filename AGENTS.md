# AGENTS.md

Guidance for LLM/code agents contributing to this repository.

## Project Summary

- Name: `SqlAudit`
- Stack: .NET 10, C#
- Solution file: `SqlAudit.slnx`
- Purpose: SQL Server schema and performance health analysis CLI with Markdown/JSON reports and SQL fix scripts.
- **Read-only by design** — SqlAudit never modifies the target database. All SQL fix scripts are written to disk for the operator to review and apply manually.

## Repository Layout

- `src/SqlAudit.Core` - domain models, health-check abstractions, execution pipeline.
- `src/SqlAudit.SqlServer` - SQL Server snapshot collection and health checks.
- `src/SqlAudit.Reporting` - Markdown/JSON/fix script rendering.
- `src/SqlAudit.Cli` - command-line parsing, config resolution, runtime orchestration.
- `tests/SqlAudit.Tests` - xUnit tests (xunit.v3).
- `project-config/` - example preset config files (also embedded in CLI assembly).

## Common Commands

- Build: `dotnet build "SqlAudit.slnx"`
- Test: `dotnet test "SqlAudit.slnx"`
- Coverage (optional): `dotnet test "SqlAudit.slnx" --collect:"XPlat Code Coverage"`

## Coding Expectations

- Follow existing naming and style conventions in the touched project.
- Keep warnings at zero; analyzer cleanliness matters in this repo.
- Prefer small, focused changes over broad rewrites.
- Add or update tests for behavior changes.
- Keep docs (`README.md`) aligned when user-visible behavior changes.

## Key Architecture Notes

- **Check counts**: Quick profile = 44 checks, Deep profile = 53 checks. `AuditReport.SchemaVersion = "1.5"`.
- **Fix script output**: individual scripts are split into `fixes/no-window/` (safe to apply without downtime) and `fixes/requires-window/` (need a maintenance window). `fixes/all-fixes.sql` is the combined bundle.
- **Edition-aware fix scripts**: `DatabaseSnapshot.SupportsOnlineIndexOperations` is `true` for Enterprise, Developer, and Azure SQL. Fix scripts for index/table rebuilds use `ONLINE = ON` or `ONLINE = OFF` accordingly.
- **All CLI console output** goes through `ScanOutput.cs` (Spectre.Console 0.49.1). `Program.cs` never calls `Console.*` directly.
- **`ServiceWindow`** is required on every `AuditFinding` — forgetting it causes CS9035.
- **`TryReadOptionalListAsync`** wraps DMV reads that may fail due to permissions; failures produce a `CollectionWarning` in the snapshot rather than aborting the scan.
- **`ApplyExclusions`** in `SqlServerSnapshotCollector.cs` and **`ApplySuppressionResult`** / **`AttachGrowthForecasts`** in `Program.cs` must be updated whenever new snapshot/report properties are added.

## Config and Presets

- `--config` supports file paths and embedded aliases:
  - `preset:quick`
  - `preset:deep`
  - `preset:deep-strict`
- Treat example root JSON files as local-only (root `/*.json` is gitignored).

## Required Agent Workflow

After each code change batch:

1. Run all tests: `dotnet test "SqlAudit.slnx"`
2. Confirm tests pass.
3. Commit the change with a clear message.
4. Push to remote.

Do not skip validation before commit/push.
