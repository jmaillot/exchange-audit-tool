# Exchange Audit Tool

Modern WinForms GUI to run Exchange Online and on-premises audit exports.

Download the latest `ExchangeAuditTool_<version>.exe` from
[GitHub Releases](https://github.com/jmaillot/exchange-audit-tool/releases) -
no installation needed, just launch it.

## What it does

Connects to Exchange (Online or on-premises), lets you tick the properties you need per object type, runs the audit, and exports `;`-delimited UTF-8 CSV files (plus a formatted `.xlsx` workbook) with a 200-row in-app preview.

Connection modes:

- Exchange Online interactive (sign in with your account in the browser, optional UPN)
- Exchange Online app-only (client ID + certificate + tenant, no interaction)
- On-premises local (run the tool directly on an Exchange server)
- On-premises remote (connects to `http(s)://<server>/PowerShell/`, Kerberos or Basic)

## Audit coverage (17 sections)

| Area | Sections |
|---|---|
| User mailboxes | User mailboxes (quotas, retention, archive, permissions, folder permissions, size, item counts), mobile devices |
| Special mailboxes | Shared, room and equipment mailboxes (delegates, calendar processing, folder permissions) |
| Groups | Distribution, security, dynamic and Microsoft 365 groups (owners, members) |
| Contacts | Mail users, mail contacts |
| Domains / Routing | Transport rules, accepted domains, remote domains, connectors |
| Public Folders | PF mailboxes, folder hierarchy (+ client permissions), mail-enabled public folders |
| Organization | Sharing, relationships, federation, org config, address policies, journal rules, retention, address books, certificates, OWA/role policies |

## Requirements

- Windows 10/11 with .NET Framework 4.8 (preinstalled on up-to-date Windows)
- For Exchange Online: the `ExchangeOnlineManagement` module - the app can install it for you from the Connection page
- An Exchange account with read rights to run `Get-*` commands (read-only audit; the only writes are the exported files), e.g. View-Only Organization Management

## User guide

1. Launch `ExchangeAuditTool_<version>.exe`. The `◐` button in the title bar switches between the dark and light theme (remembered for next launch).
2. Connection page: pick a mode, fill the fields, `Connect`. The footer confirms who you are connected as. Use `Save` in the Profile row to remember the fields under a name - next launch restores your last-used profile automatically. Passwords are never stored.
3. Pick a section in the sidebar (e.g. User mailboxes), tick the properties you need (use `Filter` to find them, `Select all` to toggle), set the Output CSV (default `%USERPROFILE%\Documents\ExchangeAudit\`), `RUN AUDIT`. Each run has its own Cancel and live progress in its Results header.
4. On success the CSV is previewed in the grid (first 200 rows). `Open CSV` opens the file, `Folder` opens its location. With the `XLSX` box checked (default), a formatted workbook (bold header, filter, frozen top row) is saved next to the CSV and the `XLSX` button opens it.

Notes:

- Your audit files land in `%USERPROFILE%\Documents\ExchangeAudit\` by default.
- Timeouts: connect 5 min, audit up to 30 min.
- The Activity Log at the bottom shows everything the tool does; it is also saved to `%USERPROFILE%\Documents\ExchangeAudit\ExchangeAuditTool.activity.log`.
- Multi-value cells are `,`-joined inside the `;`-delimited file, so Excel splits columns correctly.

## Excel import (only needed for the raw CSV)

If you unchecked `XLSX`, import the `;`-delimited UTF-8 CSV manually:

1. Excel > Data > From Text/CSV, select the exported CSV.
2. Set File Origin to `65001: Unicode (UTF-8)` and Delimiter to `Semicolon`.
3. Load.

Tip: leave the `XLSX` box checked and you can skip this entirely - the workbook opens directly.

## For developers

See [BUILD.md](BUILD.md) for building from source, project structure and the release process.

## License

MIT — see `LICENSE`.
