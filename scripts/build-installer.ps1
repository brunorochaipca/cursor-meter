$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src\CursorMeter.App\CursorMeter.App.csproj"
$publish = Join-Path $root "src\CursorMeter.App\bin\Release\net8.0\win-x64\publish"
$iss = Join-Path $PSScriptRoot "CursorMeter.iss"

$version = (dotnet msbuild $project -getProperty:Version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or -not $version) {
    throw "Could not read the version from $project"
}

# Inno Setup wants four numbers: drop a pre-release suffix such as -beta.1 and pad to four parts.
$numeric = @($version.Split("-")[0].Split("."))
while ($numeric.Count -lt 4) { $numeric += "0" }
$versionInfo = ($numeric | Select-Object -First 4) -join "."

& (Join-Path $PSScriptRoot "build-windows.ps1")

$exe = Join-Path $publish "CursorMeter.exe"
if (-not (Test-Path $exe)) {
    throw "Published executable was not found: $exe"
}

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php"
}

& $iscc "/DAppVersion=$version" "/DVersionInfoVersion=$versionInfo" "/DPublishDir=$publish" $iss
if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe failed with exit code $LASTEXITCODE"
}

$setup = Join-Path $root "dist\CursorMeter-Setup-$version.exe"
if (-not (Test-Path $setup)) {
    throw "Installer was not created: $setup"
}

Write-Host "Installer: $setup"
