# Changelog

All notable changes to this project are documented here. Format follows Keep a Changelog (newest on top).

## [Unreleased]

## [1.4.0] - 2026-09-08

### Added

- Direct `.xlsx` export beside every CSV (bold header, autofilter, frozen top row, sized columns, truncation guard at Excel limits) via a dependency-free `XlsxWriter` - no Excel install, no NuGet; opt-out per section with the `XLSX` checkbox, `XLSX` button opens the workbook

### Fixed

- XLSX writer delegates content types/relationships to the packaging API instead of colliding hand-written parts, emits `cols` before `sheetData`, and surfaces failures in the result line instead of the log only

## [1.3.0] - 2026-09-08

### Added

- Parallel section audits: each section runs on its own PowerShell process (up to 3 concurrent, extras queue with visible status), per-section Cancel, live per-section progress
- Serialized worker sign-in: parallel audits prompt once and follow-ups reuse the cached session instead of prompting per worker
- Single sign-in guarantee: modes that can prompt (interactive, Basic/credential dialog) run audits on the connected session one at a time instead of prompting per worker

## [1.2.2] - 2026-09-08

### Added

- Select-all now toggles only filter-matching options while a filter is typed
- xUnit coverage for connection/CSV validators (`IsValidUpn/Thumbprint/Hostname/CsvPath`)

### Fixed

- Scope badge truncates with ellipsis + tooltip instead of clipping
- Group hints wrap to full text at 8.5pt instead of single-line clipping at 7.8pt
- Minimum info-text size raised to 8.5pt (section subtitles, field notes, slow hint, results)
- EXO module check failure shows "check failed - retry" in place instead of surfacing through the crash log

### Changed

- Card corner radius unified to 8 (window 9 > cards 8 > groups/buttons 6)
- `Surface2` lightened for visible card-in-card separation
- Sidebar credits folded into the version tooltip, freeing nav space
- EXO module check failure shows "check failed - retry" in place instead of surfacing through the crash log

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
