[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "dist"),
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "src/ExchangeAuditTool/ExchangeAuditTool.csproj"
$programCs = Join-Path $PSScriptRoot "src/ExchangeAuditTool/Program.cs"
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Project not found: $project" }
if (-not (Test-Path -LiteralPath $programCs -PathType Leaf)) { throw "Program.cs not found: $programCs" }

# Derive the version from Program.cs (AssemblyFileVersion) so the executable name
# stays in sync automatically. Falls back to AssemblyVersion, then to "0.0.0".
$programText = Get-Content -LiteralPath $programCs -Raw
$version = "0.0.0"
$verMatch = [regex]::Match($programText, 'AssemblyFileVersion\("([0-9]+(?:\.[0-9]+){1,3})"\)')
if (-not $verMatch.Success) {
    $verMatch = [regex]::Match($programText, 'AssemblyVersion\("([0-9]+(?:\.[0-9]+){1,3})"\)')
}
if ($verMatch.Success) {
    $version = $verMatch.Groups[1].Value
    # Drop only a 4-part revision suffix for a cleaner name (1.0.0.0 -> 1.0.0),
    # while leaving 3-part versions (e.g. 1.0.0) untouched.
    $version = ($version -replace '^(\d+\.\d+\.\d+)\.\d+$', '$1')
}

$exeName = "ExchangeAuditTool_$version.exe"
$exe = Join-Path $OutputDirectory $exeName

if (Test-Path -LiteralPath $OutputDirectory) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    $publishDir = Join-Path $OutputDirectory "publish"
    & dotnet publish $project -c $Configuration -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
    $built = Join-Path $publishDir "ExchangeAuditTool.exe"
    if (-not (Test-Path -LiteralPath $built -PathType Leaf)) { throw "Expected output not found: $built" }
    Move-Item -LiteralPath $built -Destination $exe
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
else {
    # Fallback for machines with only the .NET Framework compiler and no SDK.
    $srcDir = Join-Path $PSScriptRoot "src/ExchangeAuditTool"
    $sources = Get-ChildItem -LiteralPath $srcDir -Filter *.cs -File |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } |
        Sort-Object FullName |
        Select-Object -ExpandProperty FullName
    $manifest = Join-Path $srcDir "app.manifest"
    $icon = Join-Path $srcDir "app.ico"
    foreach ($file in ($sources + $manifest + $icon)) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
            throw "Required build input was not found: $file"
        }
    }
    $cscCandidates = @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )
    $csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $csc) { throw "Neither dotnet nor the .NET Framework C# compiler csc.exe was found." }
    $cscArgs = @(
        "/nologo",
        "/target:winexe",
        "/optimize+",
        "/codepage:65001",
        "/platform:anycpu",
        "/win32manifest:$manifest",
        "/win32icon:$icon",
        "/reference:System.dll",
        "/reference:System.Core.dll",
        "/reference:System.Drawing.dll",
        "/reference:System.Windows.Forms.dll",
        "/out:$exe"
    ) + $sources
    & $csc @cscArgs
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw "Compilation failed with exit code $LASTEXITCODE."
    }
}

Write-Host ""
Write-Host "Build completed successfully." -ForegroundColor Green
Write-Host ("Version    : {0}" -f $version)
Write-Host ("Executable : {0}" -f $exe)
