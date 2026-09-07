# Contributing

## Workflow

1. Branch from `main` (`feature/...`, `fix/...`, `chore/...`).
2. Keep changes small and atomic; one concern per commit.
3. Open a pull request against `main`. CI must be green.

## Build and test (Windows)

```powershell
dotnet build ExchangeAuditTool.sln -c Release
dotnet test ExchangeAuditTool.sln -c Release
.\Build-ExchangeAuditTool.ps1
```

## Conventions

- C# 7.3 compatible code (`LangVersion` is pinned in `Directory.Build.props`).
- New audit logic goes in `src/ExchangeAuditTool/Sections.*.cs` reusing `PsScriptHelpers.cs`.
- Pure logic gets xUnit coverage in `tests/ExchangeAuditTool.Tests`.
- Document user-facing changes in `CHANGELOG.md` under `[Unreleased]`.
