# Contributing

## Build and Test

- Build: `dotnet build`
- Test: `dotnet test`

## Analyzer and Warning Consistency

This repository is configured so Visual Studio live diagnostics and `dotnet build` results stay aligned.

- Shared analyzer settings are defined in `Directory.Build.props`.
- Each project sets warning level to `9999` for Debug and Release.

If you see warnings in the IDE, run `dotnet build` to verify they reproduce in the same configuration. If they do not, check the active build configuration and Error List filters in Visual Studio.
