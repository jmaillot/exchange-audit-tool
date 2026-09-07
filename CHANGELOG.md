# Changelog

All notable changes to this project are documented here. Format follows Keep a Changelog (newest on top).

## [Unreleased]

### Fixed

- Window/taskbar icon now uses the real `app.ico` (embedded resource) instead of the generated emblem

### Changed

- Standard solution layout: sources under `src/ExchangeAuditTool`, xUnit tests under `tests/ExchangeAuditTool.Tests`
- SDK-style `net48` project; build via `dotnet publish` with `csc` fallback
- Shared PS helpers deduplicated into `PsScriptHelpers.cs`

## [1.0.0] - 2026-09-07

### Added

- Initial release: 17 Exchange audit sections (mailboxes, groups, contacts, domains/routing, public folders)
- 4 connection modes (Exchange Online interactive/app-only, on-premises local/remote)
- `;`-delimited UTF-8 CSV exports with in-app preview
