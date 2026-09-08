# Building Exchange Audit Tool

Developer notes: building from source, project layout and releases.
End users should start with [README.md](README.md) instead.

## Clone

```bash
git clone https://github.com/jmaillot/exchange-audit-tool.git
cd exchange-audit-tool
```

## Build

Prerequisites: [.NET SDK](https://dotnet.microsoft.com/download) (any recent version; the app targets `net48`) on Windows for a full build, or plain .NET Framework with `csc.exe` (at `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) for the fallback path.

```powershell
.\Build-ExchangeAuditTool.ps1
# optional: .\Build-ExchangeAuditTool.ps1 -OutputDirectory .\dist -Configuration Release
```

This runs `dotnet publish` on `src/ExchangeAuditTool` (falls back to direct `csc` if no SDK is present).
Developers can also work with the solution directly:

```powershell
dotnet build ExchangeAuditTool.sln -c Release
dotnet test ExchangeAuditTool.sln -c Release
```

The build references `System`, `System.Core`, `System.Drawing`, `System.Windows.Forms`, `System.Xml` and `WindowsBase` (in-box Framework assemblies - no NuGet packages for the app itself).
New dependencies must stay in-box so the `csc` fallback keeps working.

Output: `dist/ExchangeAuditTool_<version>.exe` (version taken from `AssemblyFileVersion` in `src/ExchangeAuditTool/Program.cs`). `dist/` is git-ignored.

## Release

1. Bump `AssemblyFileVersion` (and `AssemblyVersion`) in `src/ExchangeAuditTool/Program.cs` **before** tagging, so the first build already carries the right number.
2. Move the `Unreleased` CHANGELOG entries under a new version section.
3. Commit the version bump.
4. Tag: `git tag vX.Y.Z`, then `git push origin vX.Y.Z`.
5. The `Release` workflow builds the exe, writes SHA256 checksums (`dist/*.sha256`), warns (non-failing) if the tag does not match `AssemblyFileVersion`, and attaches `dist/*.exe` + `dist/*.sha256` to the GitHub release.

Preview builds use tags like `vX.Y.Z-preview.N` (the release is marked as pre-release and the exe gets the tag suffix).

## Project structure

```
ExchangeAuditTool.sln
src/ExchangeAuditTool/      → App (net48 WinForms)
tests/ExchangeAuditTool.Tests/ → xUnit tests for pure logic
docs/                        → Additional documentation
```

| File | Purpose |
|---|---|
| `src/ExchangeAuditTool/Program.cs` | Entry point + `MainForm` shell (title bar, sidebar nav, workspace, log card) |
| `src/ExchangeAuditTool/SectionPages.cs` | Connection page, per-section pages, `RunSectionAsync`, CSV preview |
| `src/ExchangeAuditTool/AuditModel.cs` | `AuditSection` / `AuditOptionGroup` / `AuditSelection` / `ScriptContext` DSL + `AuditRegistry.BuildAll()` |
| `src/ExchangeAuditTool/ExchangeConnection.cs` | `ConnectionMode`, `ConnectionSettings`, prelude / disconnect / status script builders |
| `src/ExchangeAuditTool/PowerShellSession.cs` | Persistent `powershell.exe` host, `Execute(script, timeout, onLine)` |
| `src/ExchangeAuditTool/PsScriptHelpers.cs` | Shared PS emitters (`Resolve-Recip`, `Get-SizeMB`), size group, collector |
| `src/ExchangeAuditTool/XlsxWriter.cs` | Dependency-free `.xlsx` writer (WindowsBase packaging only) |
| `src/ExchangeAuditTool/Sections.Mailboxes.cs` | User mailbox export |
| `src/ExchangeAuditTool/Sections.MailboxTypes.cs` | Shared / Room / Equipment mailboxes |
| `src/ExchangeAuditTool/Sections.Groups.cs` | Distribution / Security / Dynamic / M365 groups |
| `src/ExchangeAuditTool/Sections.Contacts.cs` | Mail users / Mail contacts |
| `src/ExchangeAuditTool/Sections.DomainsRouting.cs` | Transport rules / Accepted / Remote domains / Connectors |
| `src/ExchangeAuditTool/Sections.PublicFolders.cs` | PF mailboxes / hierarchy / mail-enabled PF |
| `src/ExchangeAuditTool/Sections.Organization.cs` | Sharing & org / address policies / journal / retention / books / certs / OWA / roles |
| `src/ExchangeAuditTool/Sections.Protection.cs` | EOP + DLP policies and rules (online-only) |
| `src/ExchangeAuditTool/Theme.cs`, `BrandAssets.cs` | Dark theme, `RoundedPanel` / `ModernButton`, vector icons, logo |
| `src/ExchangeAuditTool/app.manifest`, `app.ico` | `asInvoker`, PerMonitorV2 DPI, Win10/11 support; app icon |
| `Build-ExchangeAuditTool.ps1` | Build script (`dotnet publish`, `csc` fallback) |

## Implementation notes

- Each section is a declarative `AuditSection` (`AuditModel.cs`): option groups rendered as checkboxes/radios, plus a `BuildScript(AuditSelection, ScriptContext)` delegate that emits the PowerShell pipeline. Scope-aware defaults (`DefOnline` / `DefOnPrem`) re-bias checkboxes after connect.
- Every audit script is prepended with `ConnectionSettings.BuildPrelude()` so session reuse / reconnect is automatic.
- Multi-value cells are `,`-joined inside a `;`-delimited file to avoid clashes (`AuditModel.cs:102`).
- Temp scripts (`%TEMP%\ExAudit-*.ps1`) are deleted after each call.
