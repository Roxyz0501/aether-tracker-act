param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$ActPath = 'C:\Program Files (x86)\Advanced Combat Tracker'
)
$ErrorActionPreference = 'Stop'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe'
if (-not (Test-Path -LiteralPath $msbuild)) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) { $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1 }
}
if (-not $msbuild -or -not (Test-Path -LiteralPath $msbuild)) { throw "MSBuild was not found. Install Visual Studio Build Tools." }
if (-not (Test-Path -LiteralPath (Join-Path $ActPath 'Advanced Combat Tracker.exe'))) { throw "ACT reference was not found: $ActPath" }
& $msbuild CharacterTracker.sln /t:Rebuild /p:Configuration=$Configuration /p:Platform='Any CPU' /p:ACTPath=$ActPath /m:1 /nr:false /p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& "tests\CharacterTracker.Tests\bin\$Configuration\CharacterTracker.Tests.exe"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Built: dist\CharacterTracker.dll"
