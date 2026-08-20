$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet restore .\RockyBackupWinForms.csproj

dotnet publish .\RockyBackupWinForms.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\publish

$exe = Join-Path $PSScriptRoot 'publish\RockyBackup.exe'
if (-not (Test-Path $exe)) {
  throw "Build completed but RockyBackup.exe was not found: $exe"
}

Write-Host "Build OK: $exe"
Get-Item $exe | Format-List FullName,Length,LastWriteTime
