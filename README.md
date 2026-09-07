# Exchange Audit Tool

Modern WinForms GUI to run Exchange Online and on-premises audit exports.

Clone:

```bash
git clone https://github.com/jmaillot/exchange-audit-tool.git
cd exchange-audit-tool
```

## What it does

Connects to Exchange (Online or on-premises), lets you tick the properties you need per object type, generates PowerShell on the fly, runs it in a persistent session, and exports `;`-delimited UTF-8 CSV files with a 200-row in-app preview.

Connection modes (`ExchangeConnection.cs`):

- Exchange Online interactive (`Connect-ExchangeOnline`, optional UPN, `-DisableWAM` by default)
- Exchange Online app-only (`-AppId -CertificateThumbprint -Organization`)
- On-premises local (loads `RemoteExchange.ps1` / `Microsoft.Exchange.Management.PowerShell.E2010` snap-in, must run on an Exchange server)
- On-premises remote (implicit remoting to `http(s)://<server>/PowerShell/`, Kerberos or Basic)

## Audit coverage (17 sections)

| Area | Sections |
|---|---|
| User mailboxes | `mailbox-list` (UserMailbox, quotas, retention, archive, permissions, size via `Get-EXOMailboxStatistics`) |
| Special mailboxes | `shared-mailboxes`, `room-mailboxes`, `equipment-mailboxes` (`Get-Mailbox`, `Get-CalendarProcessing`, `Get-Place`, `Get-MailboxFolderPermission`) |
| Groups | `distribution-groups`, `security-groups`, `dynamic-groups`, `m365-groups` (`Get-DistributionGroup`, `Get-DynamicDistributionGroup`, `Get-UnifiedGroup` + `Get-UnifiedGroupLinks`) |
| Contacts | `mail-users` (`Get-MailUser`), `mail-contacts` (`Get-MailContact` + `Get-Contact`) |
| Domains / Routing | `transport-rules` (`Get-TransportRule`), `accepted-domains`, `remote-domains`, `connectors` (`Get-Inbound/OutboundConnector` online, `Get-Receive/SendConnector` on-prem) |
| Public Folders | `pf-mailboxes` (`Get-Mailbox -PublicFolder`), `pf-hierarchy` (`Get-PublicFolder -Recurse` + client permissions), `mail-pf` (`Get-MailPublicFolder`) |

Each section is a declarative `AuditSection` (`AuditModel.cs`): option groups rendered as checkboxes/radios, plus a `BuildScript(AuditSelection, ScriptContext)` delegate that emits the PowerShell pipeline. Scope-aware defaults (`DefOnline` / `DefOnPrem`) re-bias checkboxes after connect.

## Requirements

- Windows with .NET Framework 4.x (`csc.exe` at `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`)
- `powershell.exe` available (used as a persistent child process)
- For Exchange Online: `ExchangeOnlineManagement` module, any recent version (the app can install it from the Connection page via `Install-Module -Scope CurrentUser`)
- Exchange permissions to run `Get-*` cmdlets (read-only audit, no writes except `Export-Csv`); e.g. View-Only Organization Management, or Recipient Management read access

## Build

No `.csproj` / `.sln`, no NuGet. Direct `csc` invocation:

```powershell
.\Build-ExchangeAuditTool.ps1
# optional: .\Build-ExchangeAuditTool.ps1 -OutputDirectory .\dist
```

Output: `dist/ExchangeAuditTool_<version>.exe` (single-file `winexe`, version taken from `AssemblyFileVersion` in `Program.cs`). `dist/` is git-ignored.

## Release

1. Bump `AssemblyFileVersion` (and `AssemblyVersion`) in `Program.cs`.
2. Commit the version bump.
3. Tag: `git tag vX.Y.Z`, then `git push origin vX.Y.Z`.
4. The `Release` workflow builds the exe, writes SHA256 checksums (`dist/*.sha256`), warns (non-failing) if the tag does not match `AssemblyFileVersion`, and attaches `dist/*.exe` + `dist/*.sha256` to the GitHub release.

## Excel import (`;`-delimited UTF-8 CSV)

1. Excel > Data > From Text/CSV, select the exported CSV.
2. Set File Origin to `65001: Unicode (UTF-8)` and Delimiter to `Semicolon`.
3. Load. Multi-value cells are `,`-joined inside the `;`-delimited file.

## Run / Use

1. Launch `ExchangeAuditTool_<version>.exe`.
2. Connection page: pick a mode, fill UPN / AppId / server fields, `Connect`. Status is verified with `Get-ConnectionInformation` / `Get-OrganizationConfig`.
3. Pick a section in the sidebar (e.g. User mailboxes), tick properties, set Output CSV (default `%USERPROFILE%\Documents\ExchangeAudit\`), `RUN AUDIT`.
4. Watch the Activity Log (also written to `ExchangeAuditTool.activity.log`). On success the CSV is written by PowerShell (`Export-Csv -Delimiter ';'`) and previewed in the grid. `Open CSV` opens it with the default handler.

Notes:

- Every audit script is prepended with `ConnectionSettings.BuildPrelude()` so session reuse / reconnect is automatic.
- Multi-value cells are `,`-joined inside a `;`-delimited file to avoid clashes (`AuditModel.cs:102`).
- Timeouts: connect 5 min, audit up to 30 min. Temp scripts (`%TEMP%\ExAudit-*.ps1`) are deleted after each call.
- Logs: startup errors → `<exe_dir>\ExchangeAuditTool.startup.log`; runtime → `%USERPROFILE%\Documents\ExchangeAudit\ExchangeAuditTool.activity.log`.

## Project structure

| File | Purpose |
|---|---|
| `Program.cs` | Entry point + `MainForm` shell (title bar, sidebar nav, workspace, log card) |
| `SectionPages.cs` | Connection page, per-section pages, `RunSectionAsync`, CSV preview |
| `AuditModel.cs` | `AuditSection` / `AuditOptionGroup` / `AuditSelection` / `ScriptContext` DSL + `AuditRegistry.BuildAll()` |
| `ExchangeConnection.cs` | `ConnectionMode`, `ConnectionSettings`, prelude / disconnect / status script builders |
| `PowerShellSession.cs` | Persistent `powershell.exe` host, `Execute(script, timeout, onLine)` |
| `Sections.Mailboxes.cs` | User mailbox export |
| `Sections.MailboxTypes.cs` | Shared / Room / Equipment mailboxes |
| `Sections.Groups.cs` | Distribution / Security / Dynamic / M365 groups |
| `Sections.Contacts.cs` | Mail users / Mail contacts |
| `Sections.DomainsRouting.cs` | Transport rules / Accepted / Remote domains / Connectors |
| `Sections.PublicFolders.cs` | PF mailboxes / hierarchy / mail-enabled PF |
| `Theme.cs`, `BrandAssets.cs` | Dark theme, `RoundedPanel` / `ModernButton`, vector icons, logo |
| `app.manifest`, `app.ico` | `asInvoker`, PerMonitorV2 DPI, Win10/11 support; app icon |
| `Build-ExchangeAuditTool.ps1` | `csc` build script |

## License

MIT — see `LICENSE` (copy of `LICENCE`).
