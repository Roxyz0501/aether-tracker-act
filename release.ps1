param(
    [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$dll = Resolve-Path 'dist\CharacterTracker.dll'
$actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion
if ($actual -ne "$Version.0") { throw "DLL version $actual does not match requested release $Version" }
$updater = Resolve-Path 'dist\CharacterTracker.Updater.exe'
$updaterInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($updater)
if ($updaterInfo.FileVersion -ne "$Version.0" -or $updaterInfo.CompanyName -ne 'Roxyz0501') { throw "Updater version/author does not match requested release $Version" }

$baseName = "Aether-Tracker-ACT-v$Version"
$zip = Join-Path 'dist' "$baseName.zip"
$manifest = Join-Path 'dist' "$baseName.sha256"
$packageDirectory = Join-Path 'dist' ("package-v" + $Version)
if (Test-Path -LiteralPath $packageDirectory) { Remove-Item -LiteralPath $packageDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $packageDirectory | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $packageDirectory 'CharacterTracker.dll')
Copy-Item -LiteralPath $updater -Destination (Join-Path $packageDirectory 'CharacterTracker.Updater.exe')
Copy-Item -LiteralPath 'README.md','UI_COMMON_SPEC.md' -Destination $packageDirectory
Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $zip -Force
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $zip).Path)
try { $zipEntries = @($archive.Entries | ForEach-Object { $_.FullName }) } finally { $archive.Dispose() }
$allowed = @('CharacterTracker.dll','CharacterTracker.Updater.exe','README.md','UI_COMMON_SPEC.md')
if (@($zipEntries | Where-Object { $_ -notin $allowed }).Count -gt 0 -or @($allowed | Where-Object { $_ -notin $zipEntries }).Count -gt 0) { throw "Release ZIP contents do not match the update client's allowlist: $($zipEntries -join ', ')" }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path (Get-Location) $manifest), "$hash  $baseName.zip`r`n", (New-Object Text.UTF8Encoding($false)))
Write-Host "Release assets: $zip, $manifest"
