# Changelog

All notable changes to this project are documented here. Format follows Keep a Changelog (newest on top).

## [Unreleased]

## [1.2.1] - 2026-09-08

### Added

- Per-section option filter with match highlight and empty-group hiding
- Live streamed line count in the footer during audit runs (`Running X... mm:ss · N lines`)
- Connection input validation (UPN shape, AppId GUID, tenant, thumbprint hex, remote FQDN) with field focus
- CSV output path validation before run
- Select-all tri-state label (`Deselect all` / `Select all (n/m)`)
- Full-path tooltip on result info; clickable empty state focusing Run

### Fixed

- Connection page auto-layout: flow panels replace absolute positions (mode radios, auth/device/log rows, buttons), card grows instead of clipping
- Disabled buttons render dimmed instead of looking clickable; custom focus ring
- 32px touch targets (Re-check, Open CSV/Folder, category headers, Clear, Select-all)
- Activity log horizontal scroll; progress bar visible only when busy
- Result info truncation via ellipsis + tooltip

### Changed

- Bump to 1.2.1.0
- Nav icons cached (no per-page-switch redraw); rounded regions update on resize, not per paint
- Slow-run hint driven by explicit `Slow` flags on the audit model instead of string matching
- Custom window buttons expose accessible names

## [1.2.0] - 2026-09-08

### Added

- Cancellable audit runs with elapsed timer and run summaries (rows, size, duration)
- Sortable/copyable results grid, Folder button, empty state, slow-option warning
- Resizable options/results splitter, keyboard navigation, status glyphs
- Segoe MDL2 Assets nav icons, refreshed sidebar type, chevron category headers
- Crash log with stack traces for unhandled errors

### Fixed

- Splitter startup crash from pre-layout min sizes

## [1.1.0] - 2026-09-07

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
