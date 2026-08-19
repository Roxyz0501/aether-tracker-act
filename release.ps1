param(
    [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) { & powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 }
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dll = Resolve-Path 'dist\CharacterTracker.dll'
$actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion
if ($actual -ne "$Version.0") { throw "DLL version $actual does not match requested release $Version" }

$baseName = "Aether-Tracker-ACT-v$Version"
$zip = Join-Path 'dist' "$baseName.zip"
$manifest = Join-Path 'dist' "$baseName.sha256"
Compress-Archive -Path 'dist\CharacterTracker.dll','dist\CharacterTracker.Updater.exe','README.md','UI_COMMON_SPEC.md' -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path (Get-Location) $manifest), "$hash  $baseName.zip`r`n", (New-Object Text.UTF8Encoding($false)))
Write-Host "Release assets: $zip, $manifest"
