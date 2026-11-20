$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "..\src\CursorMeter.App\CursorMeter.App.csproj"
$publish = Join-Path $PSScriptRoot "..\src\CursorMeter.App\bin\Release\net8.0\win-x64\publish\"

# Start from a clean intermediate folder, so a stale build never leaks into the published executable.
$obj = Join-Path $PSScriptRoot "..\src\CursorMeter.App\obj\Release\net8.0\win-x64"
if (Test-Path $obj) { Remove-Item $obj -Recurse -Force }

dotnet publish $project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishDir=$publish
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Published: $publish"
