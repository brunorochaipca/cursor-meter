$ErrorActionPreference = "Stop"
dotnet publish "$PSScriptRoot\..\src\CursorMeter.App\CursorMeter.App.csproj" `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}
