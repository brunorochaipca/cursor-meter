param(
    # This branch builds the Mainland edition (beta, Start11 v1/v2). The stable Island edition lives on main.
    [ValidateSet("Mainland")]
    [string]$Edition = "Mainland"
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "..\src\CursorMeter.App\CursorMeter.App.csproj"
$publish = Join-Path $PSScriptRoot "..\src\CursorMeter.App\bin\Release\net8.0\win-x64\publish-$($Edition.ToLower())\"

# Both editions share obj\, and the edition changes the compiled code, so start from a clean intermediate folder.
$obj = Join-Path $PSScriptRoot "..\src\CursorMeter.App\obj\Release\net8.0\win-x64"
if (Test-Path $obj) { Remove-Item $obj -Recurse -Force }

dotnet publish $project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:Edition=$Edition `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishDir=$publish
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Published ${Edition}: $publish"
