# AGENTS.md

Guidance for LLM/code agents contributing to this repository.

## Project Summary

- Name: `SqlAudit`
- Stack: .NET 10, C#
- Solution file: `SqlAudit.slnx`
- Purpose: SQL Server schema/index health analysis CLI with Markdown/JSON reports and SQL fix scripts.

## Repository Layout

- `src/SqlAudit.Core` - domain models, health-check abstractions, execution pipeline.
- `src/SqlAudit.SqlServer` - SQL Server snapshot collection and health checks.
- `src/SqlAudit.Reporting` - Markdown/JSON/fix script rendering.
- `src/SqlAudit.Cli` - command-line parsing, config resolution, runtime orchestration.
- `tests/SqlAudit.Tests` - xUnit tests.
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
